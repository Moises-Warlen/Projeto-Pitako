using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Infrastructure.Persistence;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Orders.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Infrastructure.Hardware;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class OrdersSecNoHistoryChecker : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class OrdersSecRealtimeNotifier : IOrderRealtimeNotifier
{
    public List<OrderRealtimeEvent> Events { get; } = [];
    public Task NotifyAsync(OrderRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(realtimeEvent);
        return Task.CompletedTask;
    }
}

public sealed class OrdersIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _catConn = new("Data Source=:memory:");
    private readonly SqliteConnection _acaiConn = new("Data Source=:memory:");
    private readonly SqliteConnection _weighConn = new("Data Source=:memory:");
    private readonly SqliteConnection _coupConn = new("Data Source=:memory:");
    private readonly SqliteConnection _delivConn = new("Data Source=:memory:");
    private readonly SqliteConnection _invConn = new("Data Source=:memory:");
    private readonly SqliteConnection _ordConn = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

    public async Task InitializeAsync()
    {
        var initCtx = new TenantContext();
        initCtx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);

        foreach (var c in new[] { _catConn, _acaiConn, _weighConn, _coupConn, _delivConn, _invConn, _ordConn })
            await c.OpenAsync();

        await using (var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_coupConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new DeliveryLogisticsDbContext(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_delivConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_invConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _catConn.DisposeAsync();
        await _acaiConn.DisposeAsync();
        await _weighConn.DisposeAsync();
        await _coupConn.DisposeAsync();
        await _delivConn.DisposeAsync();
        await _invConn.DisposeAsync();
        await _ordConn.DisposeAsync();
    }

    [Fact]
    public async Task TenantA_ShouldNeverSeeOrMutate_OrdersOfTenantB_AndRbacLeastPrivilegeMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var courierA = User.CreateEstablishmentUser(tenantA, "Entregador A", "ent@a.com", "hash", EstablishmentRole.Courier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid orderBId;
        Guid orderAId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var catB = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catConn).Options, ctxB, _clock))
        await using (var acaiB = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctxB, _clock))
        await using (var weighB = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighConn).Options, ctxB, _clock))
        await using (var coupB = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_coupConn).Options, ctxB, _clock))
        await using (var delivB = new DeliveryLogisticsDbContext(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_delivConn).Options, ctxB, _clock))
        await using (var invB = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_invConn).Options, ctxB, _clock))
        await using (var ordB = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordConn).Options, ctxB, _clock))
        {
            var catSvcB = new CatalogManagementService(new EfCategoryRepository(catB), new EfProductRepository(catB), new EfModifierGroupRepository(catB), new EfComboRepository(catB), new EfPromotionRepository(catB), new OrdersSecNoHistoryChecker(), ctxB, _clock);
            var acaiSvcB = new AcaiBuilderManagementService(new EfAcaiCupSizeRepository(acaiB), new EfAcaiBaseFlavorRepository(acaiB), new EfAcaiFreeToppingRepository(acaiB), new EfAcaiPaidAddonRepository(acaiB), ctxB);
            var weighSvcB = new WeighingService(new EfScaleDeviceRepository(weighB), new EfTarePresetRepository(weighB), new EfWeighingRecordRepository(weighB), new UnavailablePhysicalScaleTransport(), ctxB, _clock);
            var coupSvcB = new CouponsManagementService(new EfCouponRepository(coupB), ctxB, _clock);
            var delivSvcB = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(delivB), ctxB);
            var stockSvcB = new InventoryStockAndProductionService(new EfTenantCustomUnitRepository(invB), new EfSupplyItemRepository(invB), new EfTechnicalSheetRepository(invB), new EfStockBalanceRepository(invB), new EfPurchaseReceiptRepository(invB), new EfProductionOrderRepository(invB), ctxB, _clock);

            var cartSvcB = new ShoppingCartService(new EfShoppingCartRepository(ordB), catSvcB, acaiSvcB, weighSvcB, coupSvcB, ctxB, _clock);
            var orderSvcB = new OrderManagementService(new EfShoppingCartRepository(ordB), new EfOrderRepository(ordB), delivSvcB, coupSvcB, stockSvcB, weighSvcB, new OrdersSecRealtimeNotifier(), ctxB, _clock);

            var cat = (await catSvcB.CreateCategoryAsync(adminB, "Cat B")).Value;
            var prod = (await catSvcB.CreateProductAsync(adminB, cat.Id, "Item B", 40m)).Value;
            await cartSvcB.AddCatalogProductAsync("sess-b", prod.Id);
            orderBId = (await orderSvcB.CheckoutCartAsync(adminB, new CheckoutCartRequest("sess-b", OrderChannel.PdvCounter, FulfillmentMode.CounterPickup, "Cliente B"))).Value.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var catA = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catConn).Options, ctxA, _clock);
        await using var acaiA = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctxA, _clock);
        await using var weighA = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighConn).Options, ctxA, _clock);
        await using var coupA = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_coupConn).Options, ctxA, _clock);
        await using var delivA = new DeliveryLogisticsDbContext(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_delivConn).Options, ctxA, _clock);
        await using var invA = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_invConn).Options, ctxA, _clock);
        await using var ordA = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordConn).Options, ctxA, _clock);

        var catSvcA = new CatalogManagementService(new EfCategoryRepository(catA), new EfProductRepository(catA), new EfModifierGroupRepository(catA), new EfComboRepository(catA), new EfPromotionRepository(catA), new OrdersSecNoHistoryChecker(), ctxA, _clock);
        var acaiSvcA = new AcaiBuilderManagementService(new EfAcaiCupSizeRepository(acaiA), new EfAcaiBaseFlavorRepository(acaiA), new EfAcaiFreeToppingRepository(acaiA), new EfAcaiPaidAddonRepository(acaiA), ctxA);
        var weighSvcA = new WeighingService(new EfScaleDeviceRepository(weighA), new EfTarePresetRepository(weighA), new EfWeighingRecordRepository(weighA), new UnavailablePhysicalScaleTransport(), ctxA, _clock);
        var coupSvcA = new CouponsManagementService(new EfCouponRepository(coupA), ctxA, _clock);
        var delivSvcA = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(delivA), ctxA);
        var stockSvcA = new InventoryStockAndProductionService(new EfTenantCustomUnitRepository(invA), new EfSupplyItemRepository(invA), new EfTechnicalSheetRepository(invA), new EfStockBalanceRepository(invA), new EfPurchaseReceiptRepository(invA), new EfProductionOrderRepository(invA), ctxA, _clock);

        var cartSvcA = new ShoppingCartService(new EfShoppingCartRepository(ordA), catSvcA, acaiSvcA, weighSvcA, coupSvcA, ctxA, _clock);
        var orderSvcA = new OrderManagementService(new EfShoppingCartRepository(ordA), new EfOrderRepository(ordA), delivSvcA, coupSvcA, stockSvcA, weighSvcA, new OrdersSecRealtimeNotifier(), ctxA, _clock);

        // Global query filter oculta pedidos, itens e histórico de B
        Assert.Empty(await ordA.Orders.AsNoTracking().ToListAsync());
        Assert.Empty(await ordA.OrderItems.AsNoTracking().ToListAsync());
        Assert.Empty(await ordA.OrderStatusHistories.AsNoTracking().ToListAsync());

        var boardA = await orderSvcA.GetKanbanBoardAsync(adminA);
        Assert.Empty(boardA.Value.Received);

        // Admin A não consegue transicionar nem cancelar pedido de B
        var hijacked = await orderSvcA.TransitionKanbanStatusAsync(adminA, orderBId, OrderStatus.Cancelled, "Tentativa cruzada");
        Assert.True(hijacked.IsFailure);
        Assert.Equal("Order.NotFound", hijacked.Error.Code);

        // Cria pedido próprio em A e valida RBAC de menor privilégio
        var catA1 = (await catSvcA.CreateCategoryAsync(adminA, "Cat A")).Value;
        var prodA1 = (await catSvcA.CreateProductAsync(adminA, catA1.Id, "Item A", 35m)).Value;
        await cartSvcA.AddCatalogProductAsync("sess-a", prodA1.Id);
        orderAId = (await orderSvcA.CheckoutCartAsync(adminA, new CheckoutCartRequest("sess-a", OrderChannel.PdvCounter, FulfillmentMode.CounterPickup, "Cli A"))).Value.Id;

        var kitchenCancel = await orderSvcA.TransitionKanbanStatusAsync(kitchenA, orderAId, OrderStatus.Cancelled, "Cozinha tentando cancelar");
        Assert.True(kitchenCancel.IsFailure);
        Assert.Equal("Orders.Forbidden", kitchenCancel.Error.Code);

        var courierMutate = await orderSvcA.TransitionKanbanStatusAsync(courierA, orderAId, OrderStatus.Accepted, "Entregador tentando aceitar");
        Assert.True(courierMutate.IsFailure);
        Assert.Equal("Orders.Forbidden", courierMutate.Error.Code);
    }
}
