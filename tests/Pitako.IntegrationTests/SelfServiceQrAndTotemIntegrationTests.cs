using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class SelfServiceStubNoHistory : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class FixedStoreBrandingProvider(string storeTradeName) : ISelfServiceBrandingProvider
{
    public Task<SelfServiceStoreBranding?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SelfServiceStoreBranding?>(new SelfServiceStoreBranding(
            tenantId,
            storeTradeName,
            $"https://cdn.example/{tenantId:N}/logo.png",
            "#7C3AED",
            "#4C1D95",
            "#FBBF24",
            UsesPitakoBranding: false));
}

internal sealed class RecordingWaiterCallNotifier : IWaiterCallRealtimeNotifier
{
    public List<WaiterCallRealtimeEvent> Events { get; } = [];

    public Task NotifyAsync(WaiterCallRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(realtimeEvent);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingSelfServiceDispatcher : ISelfServiceOrderDispatcher
{
    private int _seq = 100;
    public List<SelfServiceOrderDispatchRequest> Dispatched { get; } = [];

    public Task<Result<SelfServiceDispatchedOrder>> DispatchAsync(
        SelfServiceOrderDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        Dispatched.Add(request);
        _seq++;
        return Task.FromResult(Result.Success(new SelfServiceDispatchedOrder(Guid.NewGuid(), $"#{_seq:D4}")));
    }
}

public sealed class SelfServiceQrAndTotemIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _catalogConn = new("Data Source=:memory:");
    private readonly SqliteConnection _acaiConn = new("Data Source=:memory:");
    private readonly SqliteConnection _diningConn = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 22, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        var initCtx = new TenantContext();
        initCtx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);

        foreach (var conn in new[] { _catalogConn, _acaiConn, _diningConn })
            await conn.OpenAsync();

        await using var catDb = CreateCatalogDb(initCtx); await catDb.Database.EnsureCreatedAsync();
        await using var acaiDb = CreateAcaiDb(initCtx); await acaiDb.Database.EnsureCreatedAsync();
        await using var dinDb = CreateDiningDb(initCtx); await dinDb.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _catalogConn.DisposeAsync();
        await _acaiConn.DisposeAsync();
        await _diningConn.DisposeAsync();
    }

    private CatalogDbContext CreateCatalogDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, ctx, _clock);

    private AcaiBuilderDbContext CreateAcaiDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctx, _clock);

    private DiningHallDbContext CreateDiningDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DiningHallDbContext>().UseSqlite(_diningConn).Options, ctx, _clock);

    [Fact]
    public async Task QrCodeTable_And_Totem_SelfService_ShouldPriceServerSide_DispatchKitchen_NotifyWaiter_AndUseStoreBrandingOnly()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente Carla", "carla@acaisul.com", "hash", EstablishmentRole.Manager).Value;
        var waiter = User.CreateEstablishmentUser(tenantId, "Garçom Beto", "beto@acaisul.com", "hash", EstablishmentRole.Waiter).Value;

        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.PublicStoreSlug);

        Guid productId;
        Guid cupSizeId;
        Guid baseFlavorId;
        Guid freeToppingId;
        Guid paidAddonId;
        Guid waiterCallId;
        string qrToken = "QR-SALAO-M01";

        var notifier = new RecordingWaiterCallNotifier();
        var dispatcher = new RecordingSelfServiceDispatcher();
        var branding = new FixedStoreBrandingProvider("Açaí do Sul");

        await using (var catDb = CreateCatalogDb(ctx))
        await using (var acaiDb = CreateAcaiDb(ctx))
        await using (var dinDb = CreateDiningDb(ctx))
        {
            var catSvc = new CatalogManagementService(
                new EfCategoryRepository(catDb),
                new EfProductRepository(catDb),
                new EfModifierGroupRepository(catDb),
                new EfComboRepository(catDb),
                new EfPromotionRepository(catDb),
                new SelfServiceStubNoHistory(),
                ctx,
                _clock);

            var acaiSvc = new AcaiBuilderManagementService(
                new EfAcaiCupSizeRepository(acaiDb),
                new EfAcaiBaseFlavorRepository(acaiDb),
                new EfAcaiFreeToppingRepository(acaiDb),
                new EfAcaiPaidAddonRepository(acaiDb),
                ctx);

            var hallSvc = new DiningHallManagementService(
                new EfDiningSectorRepository(dinDb),
                new EfDiningTableRepository(dinDb),
                new EfTableReservationRepository(dinDb),
                new EfDiningTabRepository(dinDb),
                ctx,
                _clock);

            var sector = (await hallSvc.CreateSectorAsync(manager, "Salão Central")).Value;
            await hallSvc.CreateTableAsync(manager, sector.Id, "M01", 4, qrToken);

            var cat = (await catSvc.CreateCategoryAsync(manager, "Salgados")).Value;
            var prod = (await catSvc.CreateProductAsync(manager, cat.Id, "Coxinha Especial", 9.50m)).Value;
            productId = prod.Id;

            var cup = (await acaiSvc.CreateCupSizeAsync(manager, "Copo 500ml", 500, 18.00m, 2)).Value;
            var flavor = (await acaiSvc.CreateBaseFlavorAsync(manager, "Açaí Tradicional")).Value;
            var free = (await acaiSvc.CreateFreeToppingAsync(manager, "Granola Crocante")).Value;
            var paid = (await acaiSvc.CreatePaidAddonAsync(manager, "Creme de Ninho", 4.50m)).Value;
            cupSizeId = cup.Id;
            baseFlavorId = flavor.Id;
            freeToppingId = free.Id;
            paidAddonId = paid.Id;

            var qrSvc = new QrTableSelfServiceService(
                new EfDiningTableRepository(dinDb),
                new EfDiningTabRepository(dinDb),
                new EfQrTableSessionRepository(dinDb),
                new EfWaiterCallRepository(dinDb),
                catSvc,
                acaiSvc,
                branding,
                notifier,
                dispatcher,
                ctx,
                _clock);

            // 1. Cliente lê QR Code da M01 com branding exclusivo da loja (sem marca PITAKO)
            var qrStart = (await qrSvc.StartOrResumeByQrTokenAsync(qrToken, "Mesa M01 - Ana")).Value;
            Assert.Equal("Açaí do Sul", qrStart.StoreBranding.StoreTradeName);
            Assert.False(qrStart.StoreBranding.UsesPitakoBranding);

            // 2. Lança 2 coxinhas (19,00) + 1 açaí montado (18,00 + 4,50 = 22,50) -> Sub=41,50; +10%(4,15) = 45,65
            await qrSvc.AddCatalogProductAsync(qrStart.Session.SessionToken, productId, quantity: 2);
            var qrUpdated = (await qrSvc.AddCustomAcaiAsync(
                qrStart.Session.SessionToken,
                cupSizeId,
                [baseFlavorId],
                [new AcaiItemSelection(freeToppingId, 1)],
                [new AcaiItemSelection(paidAddonId, 1)],
                quantity: 1)).Value;

            Assert.Equal(41.50m, qrUpdated.OpenTab.ItemsSubtotal.Amount);
            Assert.Equal(45.65m, qrUpdated.OpenTab.TotalAmount.Amount);
            Assert.Equal(2, dispatcher.Dispatched.Count(d => d.ChannelCode == "QrCodeTable"));

            // 3. Cliente pede a conta pelo QR -> notifica garçom e muda mesa para AwaitingPayment
            var billCall = (await qrSvc.CallWaiterAsync(qrStart.Session.SessionToken, WaiterCallType.RequestBill, "Cartão crédito")).Value;
            waiterCallId = billCall.Id;
            Assert.Single(notifier.Events);
            Assert.Equal(DiningTableStatus.AwaitingPayment, qrUpdated.Table.Status);

            // 4. Garçom atende e finaliza o chamado
            var acked = (await qrSvc.AcknowledgeWaiterCallAsync(waiter, waiterCallId)).Value;
            Assert.Equal(WaiterCallStatus.Acknowledged, acked.Status);
            var doneCall = (await qrSvc.CompleteWaiterCallAsync(waiter, waiterCallId)).Value;
            Assert.Equal(WaiterCallStatus.Completed, doneCall.Status);

            // 5. Fluxo completo de Totem de Autoatendimento com senha e idempotência
            var totemSvc = new TotemSelfServiceService(
                new EfTotemTerminalRepository(dinDb),
                new EfTotemOrderSessionRepository(dinDb),
                catSvc,
                acaiSvc,
                branding,
                dispatcher,
                ctx,
                _clock);

            await totemSvc.RegisterTerminalAsync(manager, "TOTEM-01", "Totem Balcão 1", TotemFulfillmentChoice.CounterPickup, requireCustomerName: true);
            var tStart = (await totemSvc.StartSessionAsync("TOTEM-01", TotemFulfillmentChoice.CounterPickup, "Lucas Totem")).Value;
            Assert.False(tStart.StoreBranding.UsesPitakoBranding);

            await totemSvc.AddCatalogProductAsync(tStart.Session.SessionToken, productId, quantity: 2); // 19,00
            await totemSvc.AddCustomAcaiAsync(tStart.Session.SessionToken, cupSizeId, [baseFlavorId], paidAddons: [new AcaiItemSelection(paidAddonId, 1)]); // 22,50

            var receipt1 = (await totemSvc.ConfirmCheckoutAsync(tStart.Session.SessionToken, TotemPaymentChoice.PixOnTotem, "totem-key-1")).Value;
            var receipt2 = (await totemSvc.ConfirmCheckoutAsync(tStart.Session.SessionToken, TotemPaymentChoice.PixOnTotem, "totem-key-1")).Value;

            Assert.Equal("T-0001", receipt1.PickupCallCode);
            Assert.Equal(receipt1.PickupCallCode, receipt2.PickupCallCode);
            Assert.Equal(receipt1.DispatchedOrderNumber, receipt2.DispatchedOrderNumber);
            Assert.Equal(41.50m, receipt1.Session.ItemsSubtotal.Amount);
            Assert.Single(dispatcher.Dispatched, d => d.ChannelCode == "SelfServiceTotem");
        }
    }
}
