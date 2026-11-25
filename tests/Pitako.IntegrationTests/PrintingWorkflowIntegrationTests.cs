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
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class StoreIdentityTestProvider(string tradeName, string legalName, string rawCnpj, string logoRef)
    : IStorePrintIdentityProvider
{
    public Task<Result<PrintableStoreIdentity>> GetStorePrintIdentityAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(PrintableStoreIdentity.Create(tenantId, tradeName, legalName, rawCnpj, logoRef, "Av. Central, 250", "(11) 3000-4000"));
}

internal sealed class SwitchablePrinterTransport : IThermalPrinterTransport
{
    public HashSet<Guid> FailingPrinterIds { get; } = [];
    public List<(Guid PrinterId, int Copies, int PayloadLength)> PrintedPackets { get; } = [];

    public Task<Result> SendEscPosAsync(PrinterProfile printer, byte[] escPosPayload, int copies, CancellationToken cancellationToken = default)
    {
        if (FailingPrinterIds.Contains(printer.Id))
            return Task.FromResult(Result.Failure(Error.Conflict("Printer.Timeout", $"Sem resposta TCP em {printer.EndpointOrPort}")));

        PrintedPackets.Add((printer.Id, copies, escPosPayload.Length));
        return Task.FromResult(Result.Success());
    }
}

public sealed class PrintingWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 0, 0, TimeSpan.Zero));

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
    public async Task Printing_MultiSectorRouting_AutomaticFallback_And_PhysicalUnavailableFailure_ShouldWorkCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var kitchenOp = User.CreateEstablishmentUser(tenantId, "Cozinha", "coz@loja.com", "hash", EstablishmentRole.Kitchen).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var storeProvider = new StoreIdentityTestProvider("Point do Açaí", "Point do Acai Alimentos LTDA", "11222333000181", "https://cdn.point.com/logo.png");
        var transport = new SwitchablePrinterTransport();

        Guid kitchenPrimaryId;
        Guid kitchenBackupId;
        Guid barPrinterId;
        Guid counterPrinterId;
        var drinksCategoryId = Guid.NewGuid();

        await using (var db = CreateDb(ctx))
        {
            var svc = new PrintingManagementService(
                new EfPrinterProfileRepository(db),
                new EfSectorRoutingRuleRepository(db),
                new EfPrintJobRepository(db),
                storeProvider,
                transport,
                ctx,
                _clock);

            var kBackup = (await svc.RegisterPrinterAsync(manager, "IMP-COZ-RES", "Cozinha Reserva", PrinterConnectionKind.NetworkTcpEscPos, "192.168.0.102:9100")).Value;
            var kPrimary = (await svc.RegisterPrinterAsync(manager, "IMP-COZ-01", "Cozinha Principal", PrinterConnectionKind.NetworkTcpEscPos, "192.168.0.101:9100", backupPrinterId: kBackup.Id)).Value;
            var barPrn = (await svc.RegisterPrinterAsync(manager, "IMP-BAR-01", "Bar Bebidas", PrinterConnectionKind.UsbSpooler, "USB002", PaperWidthMm.Mm58)).Value;
            var cntPrn = (await svc.RegisterPrinterAsync(manager, "IMP-BAL-01", "Balcão Expedição", PrinterConnectionKind.NetworkTcpEscPos, "192.168.0.105:9100")).Value;

            kitchenPrimaryId = kPrimary.Id;
            kitchenBackupId = kBackup.Id;
            barPrinterId = barPrn.Id;
            counterPrinterId = cntPrn.Id;

            await svc.ConfigureSectorRoutingAsync(manager, ProductionSectorType.Kitchen, kitchenPrimaryId, kitchenBackupId, copiesCount: 2);
            await svc.ConfigureSectorRoutingAsync(manager, ProductionSectorType.Bar, barPrinterId, categoryId: drinksCategoryId, copiesCount: 1);
            await svc.ConfigureSectorRoutingAsync(manager, ProductionSectorType.Counter, counterPrinterId, copiesCount: 1);

            // 1. Roteamento multi-setor: 1 prato na Cozinha + 1 bebida no Bar + conferência no Balcão
            var routeRes = await svc.RouteOrderToSectorsAsync(kitchenOp, new RouteOrderTicketsRequest(
                "#0123",
                "Delivery Próprio",
                "Fernanda",
                null,
                "Atendente Bia",
                [
                    new RoutedOrderItemInput(ProductionSectorType.Kitchen, null, "Lasanha Bolonhesa", 1, "Grande", "Bem quente", 42.00m),
                    new RoutedOrderItemInput(ProductionSectorType.Bar, drinksCategoryId, "Suco Verde", 2, "Com hortelã", null, 18.00m)
                ],
                AlsoPrintCounterConference: true,
                OrderTotalAmount: 60.00m));

            Assert.True(routeRes.IsSuccess);
            Assert.Equal(3, routeRes.Value.Count);
            Assert.All(routeRes.Value, j =>
            {
                Assert.Equal(PrintJobStatus.Printed, j.Status);
                Assert.Equal("11.222.333/0001-81", j.StoreCnpj);
                Assert.Contains("https://cdn.point.com/logo.png", j.StoreLogoRef);
                Assert.DoesNotContain("PITAKO", j.RenderedPlainText, StringComparison.OrdinalIgnoreCase);
            });

            // 2. Simula falha/queda na impressora principal da Cozinha -> fallback automático para IMP-COZ-RES
            transport.FailingPrinterIds.Add(kitchenPrimaryId);
            var fallbackJob = (await svc.PrintSectorTicketAsync(kitchenOp, new ThermalTicketPayloadInput(
                ProductionSectorType.Kitchen,
                PrintDocumentKind.ProductionKitchenTicket,
                "#0124",
                "Salão",
                "Mesa 02",
                "Mesa M02",
                "Garçom Lucas",
                _clock.UtcNow,
                [new TicketLineInput("Filé à Parmegiana", 1, "Padrão")]))).Value;

            Assert.Equal(PrintJobStatus.PrintedWithFallback, fallbackJob.Status);
            Assert.True(fallbackJob.UsedFallback);
            Assert.Equal(kitchenBackupId, fallbackJob.ActualPrinterId);
        }

        // 3. Sem agente físico conectado (UnavailablePhysicalPrinterTransport) -> registra falha explícita, nunca finge sucesso
        await using (var dbNoHw = CreateDb(ctx))
        {
            var svcNoHw = new PrintingManagementService(
                new EfPrinterProfileRepository(dbNoHw),
                new EfSectorRoutingRuleRepository(dbNoHw),
                new EfPrintJobRepository(dbNoHw),
                storeProvider,
                new UnavailablePhysicalPrinterTransport(),
                ctx,
                _clock);

            var failedJob = (await svcNoHw.PrintSectorTicketAsync(kitchenOp, new ThermalTicketPayloadInput(
                ProductionSectorType.Counter,
                PrintDocumentKind.CounterPickupConference,
                "#0125",
                "Balcão",
                "Carlos",
                null,
                null,
                _clock.UtcNow,
                [new TicketLineInput("Pastel Carne", 1, "Frito na hora")]))).Value;

            Assert.Equal(PrintJobStatus.Failed, failedJob.Status);
            Assert.Contains("não detectada", failedJob.LastError);
        }
    }
}
