using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Application.Services;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Printing.Infrastructure.Hardware;
using Pitako.Modules.Printing.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecurityStorePrintIdentityProvider : IStorePrintIdentityProvider
{
    public Task<Result<PrintableStoreIdentity>> GetStorePrintIdentityAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(PrintableStoreIdentity.Create(tenantId, "Loja Segura", "Loja Segura LTDA", "11222333000181", "https://cdn.loja/logo.png"));
}

public sealed class PrintingIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private PrintingDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<PrintingDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadOrReprint_TenantB_PrintersOrJobs_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var courierA = User.CreateEstablishmentUser(tenantA, "Entregador A", "ent@a.com", "hash", EstablishmentRole.Courier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var identityProvider = new SecurityStorePrintIdentityProvider();
        var noHwTransport = new UnavailablePhysicalPrinterTransport();

        Guid printerBId;
        Guid jobBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new PrintingManagementService(
                new EfPrinterProfileRepository(dbB),
                new EfSectorRoutingRuleRepository(dbB),
                new EfPrintJobRepository(dbB),
                identityProvider,
                noHwTransport,
                ctxB,
                _clock);

            var prnB = (await svcB.RegisterPrinterAsync(adminB, "PRN-B1", "Impressora B", PrinterConnectionKind.NetworkTcpEscPos, "10.0.0.50:9100")).Value;
            printerBId = prnB.Id;
            await svcB.ConfigureSectorRoutingAsync(adminB, ProductionSectorType.Kitchen, printerBId);

            var jobB = (await svcB.PrintSectorTicketAsync(adminB, new ThermalTicketPayloadInput(
                ProductionSectorType.Kitchen,
                PrintDocumentKind.ProductionKitchenTicket,
                "#B-01",
                "PDV",
                "Cliente B",
                null,
                null,
                _clock.UtcNow,
                [new TicketLineInput("Item B", 1, "Padrão")]))).Value;
            jobBId = jobB.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new PrintingManagementService(
            new EfPrinterProfileRepository(dbA),
            new EfSectorRoutingRuleRepository(dbA),
            new EfPrintJobRepository(dbA),
            identityProvider,
            noHwTransport,
            ctxA,
            _clock);

        // Global Query Filters isolam impressoras, regras e jobs de B
        Assert.Empty(await dbA.Printers.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.SectorRoutingRules.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.PrintJobs.AsNoTracking().ToListAsync());

        // Cross-tenant access blocked
        var crossReprint = await svcA.ReprintJobAsync(managerA, jobBId);
        Assert.True(crossReprint.IsFailure);
        Assert.Equal("PrintJob.NotFound", crossReprint.Error.Code);

        var crossStatus = await svcA.UpdatePrinterStatusAsync(managerA, printerBId, PrinterStatus.Offline);
        Assert.True(crossStatus.IsFailure);
        Assert.Equal("Printer.NotFound", crossStatus.Error.Code);

        // RBAC menor privilégio:
        // - Cozinha NÃO pode cadastrar novas impressoras (tem View|Print, não Create)
        var kitchenRegister = await svcA.RegisterPrinterAsync(kitchenA, "PRN-A1", "Imp A", PrinterConnectionKind.UsbSpooler, "USB001");
        Assert.True(kitchenRegister.IsFailure);
        Assert.Equal("Printing.Forbidden", kitchenRegister.Error.Code);

        // - Entregador NÃO tem acesso ao módulo Printing
        var courierPrint = await svcA.ReprintJobAsync(courierA, Guid.NewGuid());
        Assert.True(courierPrint.IsFailure);
        Assert.Equal("Printing.Forbidden", courierPrint.Error.Code);
    }
}
