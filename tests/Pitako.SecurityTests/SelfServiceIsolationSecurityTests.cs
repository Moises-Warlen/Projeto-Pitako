using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecurityStubPricing : ICatalogItemPricingService, IAcaiAssemblyPricingService, ISelfServiceBrandingProvider, IWaiterCallRealtimeNotifier, ISelfServiceOrderDispatcher
{
    public Task<Result<CatalogProductQuote>> QuoteProductItemAsync(Guid productId, Guid? variationId, IReadOnlyList<CatalogModifierSelection>? modifiers, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new CatalogProductQuote(productId, Guid.NewGuid(), "Item Teste", null, null, Money.Create(15m).Value, false, "Padrão", "{}")));

    public Task<Result<AcaiCupAssemblySummary>> BuildAndPriceCupAsync(AcaiCupBuildRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<AcaiCupAssemblySummary>(Error.NotFound("Acai.NotUsed", "Não usado neste teste.")));

    public Task<SelfServiceStoreBranding?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SelfServiceStoreBranding?>(null);

    public Task NotifyAsync(WaiterCallRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<Result<SelfServiceDispatchedOrder>> DispatchAsync(SelfServiceOrderDispatchRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new SelfServiceDispatchedOrder(Guid.NewGuid(), "#0001")));
}

public sealed class SelfServiceIsolationSecurityTests : IAsyncLifetime
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

    private DiningHallDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DiningHallDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldBeIsolatedFromTenantB_QrSessions_WaiterCalls_Totems_And_RbacMustBlockUnauthorizedRoles()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var waiterA = User.CreateEstablishmentUser(tenantA, "Garçom A", "gar@a.com", "hash", EstablishmentRole.Waiter).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var stub = new SecurityStubPricing();
        string qrSessionTokenB;
        Guid waiterCallIdB;
        string totemSessionTokenB;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var hallB = new DiningHallManagementService(new EfDiningSectorRepository(dbB), new EfDiningTableRepository(dbB), new EfTableReservationRepository(dbB), new EfDiningTabRepository(dbB), ctxB, _clock);
            var qrB = new QrTableSelfServiceService(new EfDiningTableRepository(dbB), new EfDiningTabRepository(dbB), new EfQrTableSessionRepository(dbB), new EfWaiterCallRepository(dbB), stub, stub, stub, stub, stub, ctxB, _clock);
            var totemB = new TotemSelfServiceService(new EfTotemTerminalRepository(dbB), new EfTotemOrderSessionRepository(dbB), stub, stub, stub, stub, ctxB, _clock);

            var secB = (await hallB.CreateSectorAsync(adminB, "Salão B")).Value;
            await hallB.CreateTableAsync(adminB, secB.Id, "B01", 4, "QR-B-SECRET");
            var qrSessB = (await qrB.StartOrResumeByQrTokenAsync("QR-B-SECRET", "Cliente B")).Value;
            qrSessionTokenB = qrSessB.Session.SessionToken;
            var callB = (await qrB.CallWaiterAsync(qrSessionTokenB, WaiterCallType.CallWaiter)).Value;
            waiterCallIdB = callB.Id;

            await totemB.RegisterTerminalAsync(adminB, "TOTEM-B1", "Totem B");
            var tSessB = (await totemB.StartSessionAsync("TOTEM-B1", customerName: "Cliente Totem B")).Value;
            totemSessionTokenB = tSessB.Session.SessionToken;
            await totemB.AddCatalogProductAsync(totemSessionTokenB, Guid.NewGuid(), quantity: 1);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var qrA = new QrTableSelfServiceService(new EfDiningTableRepository(dbA), new EfDiningTabRepository(dbA), new EfQrTableSessionRepository(dbA), new EfWaiterCallRepository(dbA), stub, stub, stub, stub, stub, ctxA, _clock);
        var totemA = new TotemSelfServiceService(new EfTotemTerminalRepository(dbA), new EfTotemOrderSessionRepository(dbA), stub, stub, stub, stub, ctxA, _clock);

        // Global Query Filter oculta todas as novas entidades da Loja B
        Assert.Empty(await dbA.QrTableSessions.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.WaiterCalls.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.TotemTerminals.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.TotemOrderSessions.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.TotemOrderLines.AsNoTracking().ToListAsync());

        // Cross-tenant token/id attacks falham com NotFound
        var crossQr = await qrA.GetSummaryAsync(qrSessionTokenB);
        Assert.True(crossQr.IsFailure);
        Assert.Equal("QrSession.NotFound", crossQr.Error.Code);

        var crossCall = await qrA.AcknowledgeWaiterCallAsync(managerA, waiterCallIdB);
        Assert.True(crossCall.IsFailure);
        Assert.Equal("WaiterCall.NotFound", crossCall.Error.Code);

        var crossTotem = await totemA.ConfirmCheckoutAsync(totemSessionTokenB, TotemPaymentChoice.PayAtCashier);
        Assert.True(crossTotem.IsFailure);
        Assert.Equal("Totem.SessionNotFound", crossTotem.Error.Code);

        // Menor privilégio RBAC:
        // - Garçom NÃO pode cadastrar terminal de Totem (exige PermissionAction.Approve -> Gerente/Admin)
        var waiterRegisterTotem = await totemA.RegisterTerminalAsync(waiterA, "TOTEM-A1", "Totem A");
        Assert.True(waiterRegisterTotem.IsFailure);
        Assert.Equal("Dining.Forbidden", waiterRegisterTotem.Error.Code);

        // - Cozinha NÃO pode atender chamado de garçom
        var kitchenAck = await qrA.AcknowledgeWaiterCallAsync(kitchenA, Guid.NewGuid());
        Assert.True(kitchenAck.IsFailure);
        Assert.Equal("Dining.Forbidden", kitchenAck.Error.Code);
    }
}
