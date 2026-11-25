using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.Coupons.Infrastructure.Persistence;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
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
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class RecordingOrderRealtimeNotifier : IOrderRealtimeNotifier
{
    public List<OrderRealtimeEvent> Events { get; } = [];

    public Task NotifyAsync(OrderRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(realtimeEvent);
        return Task.CompletedTask;
    }
}

public sealed class OrdersCheckoutAndKanbanIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _catalogConn = new("Data Source=:memory:");
    private readonly SqliteConnection _acaiConn = new("Data Source=:memory:");
    private readonly SqliteConnection _weighConn = new("Data Source=:memory:");
    private readonly SqliteConnection _couponConn = new("Data Source=:memory:");
    private readonly SqliteConnection _delivConn = new("Data Source=:memory:");
    private readonly SqliteConnection _invConn = new("Data Source=:memory:");
    private readonly SqliteConnection _ordersConn = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 17, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        var initCtx = new TenantContext();
        initCtx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);

        foreach (var c in new[] { _catalogConn, _acaiConn, _weighConn, _couponConn, _delivConn, _invConn, _ordersConn })
            await c.OpenAsync();

        await using (var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new DeliveryLogisticsDbContext(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_delivConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_invConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
        await using (var db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, initCtx, _clock)) await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _catalogConn.DisposeAsync();
        await _acaiConn.DisposeAsync();
        await _weighConn.DisposeAsync();
        await _couponConn.DisposeAsync();
        await _delivConn.DisposeAsync();
        await _invConn.DisposeAsync();
        await _ordersConn.DisposeAsync();
    }

    [Fact]
    public async Task Checkout_ShouldOrchestrateDeliveryCouponWeighingInventoryHistoryAndRealtimeKanban_AndReverseOnCancel()
    {
        var tenantId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var cashier = User.CreateEstablishmentUser(tenantId, "Caixa", "caixa@loja.com", "hash", EstablishmentRole.Cashier).Value;
        var kitchen = User.CreateEstablishmentUser(tenantId, "Cozinha", "cozinha@loja.com", "hash", EstablishmentRole.Kitchen).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        await using var catDb = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, ctx, _clock);
        await using var acaiDb = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctx, _clock);
        await using var weighDb = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighConn).Options, ctx, _clock);
        await using var coupDb = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponConn).Options, ctx, _clock);
        await using var delivDb = new DeliveryLogisticsDbContext(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_delivConn).Options, ctx, _clock);
        await using var invDb = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_invConn).Options, ctx, _clock);
        await using var ordDb = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, ctx, _clock);

        var orderRepo = new EfOrderRepository(ordDb);
        var historyChecker = new OrderProductHistoryReferenceChecker(orderRepo);

        var catalogSvc = new CatalogManagementService(new EfCategoryRepository(catDb), new EfProductRepository(catDb), new EfModifierGroupRepository(catDb), new EfComboRepository(catDb), new EfPromotionRepository(catDb), historyChecker, ctx, _clock);
        var acaiSvc = new AcaiBuilderManagementService(new EfAcaiCupSizeRepository(acaiDb), new EfAcaiBaseFlavorRepository(acaiDb), new EfAcaiFreeToppingRepository(acaiDb), new EfAcaiPaidAddonRepository(acaiDb), ctx);
        var weighingSvc = new WeighingService(new EfScaleDeviceRepository(weighDb), new EfTarePresetRepository(weighDb), new EfWeighingRecordRepository(weighDb), new UnavailablePhysicalScaleTransport(), ctx, _clock);
        var couponSvc = new CouponsManagementService(new EfCouponRepository(coupDb), ctx, _clock);
        var deliverySvc = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(delivDb), ctx);

        var supplySvc = new SuppliesAndTechnicalSheetService(new EfTenantCustomUnitRepository(invDb), new EfSupplyItemRepository(invDb), new EfTechnicalSheetRepository(invDb), ctx, _clock);
        var stockSvc = new InventoryStockAndProductionService(new EfTenantCustomUnitRepository(invDb), new EfSupplyItemRepository(invDb), new EfTechnicalSheetRepository(invDb), new EfStockBalanceRepository(invDb), new EfPurchaseReceiptRepository(invDb), new EfProductionOrderRepository(invDb), ctx, _clock);

        var cartSvc = new ShoppingCartService(new EfShoppingCartRepository(ordDb), catalogSvc, acaiSvc, weighingSvc, couponSvc, ctx, _clock);
        var notifier = new RecordingOrderRealtimeNotifier();
        var orderSvc = new OrderManagementService(new EfShoppingCartRepository(ordDb), orderRepo, deliverySvc, couponSvc, stockSvc, weighingSvc, notifier, ctx, _clock);

        // Setup: Produto Burger + Insumo Carne (5000g estoque, 200g por burger)
        var cat = (await catalogSvc.CreateCategoryAsync(admin, "Burgers")).Value;
        var burger = (await catalogSvc.CreateProductAsync(admin, cat.Id, "Pitako Smash", 32.00m)).Value;
        var carne = (await supplySvc.CreateSupplyItemAsync(admin, "Blend Bovino", 40m, 1m, "kg")).Value;
        await stockSvc.ConfigureStockBalanceAsync(admin, carne.Id, "g", minimumStockQuantity: 1000m, allowNegativeStock: false, autoDeductOnOrder: true);
        await stockSvc.ReceivePurchaseAsync(admin, "Frigorífico", "NF-1", [new PurchaseLineInput(carne.Id, 5m, "kg", 40m)]);
        await supplySvc.CreateTechnicalSheetAsync(admin, "Ficha Smash", 1m, "unidade", [new IngredientInput(carne.Id, 200m, "g")], [], productId: burger.Id);

        // Açaí 500ml + Pesagem + Delivery + Cupom R$ 10 fixo
        var cup500 = (await acaiSvc.CreateCupSizeAsync(admin, "500 ml", 500, 22.00m, 5)).Value;
        var leite = (await acaiSvc.CreateFreeToppingAsync(admin, "Leite em pó", 1)).Value;
        var gran = (await acaiSvc.CreateFreeToppingAsync(admin, "Granola", 1)).Value;
        var weighRec = (await weighingSvc.RegisterManualWeighingAsync(admin, "Açaí Balança", 50m, 420m, "Contingência", manualTareGrams: 20m)).Value; // R$ 20

        var policy = (await deliverySvc.CreatePolicyAsync(admin, -23.5613, -46.6565, 8m, FreightCalculationMode.DistanceTiered, 0m, 0m, 0m, 30, 20m)).Value;
        await deliverySvc.AddDistanceTierAsync(admin, policy.Id, 0m, 5m, 9.50m, 30);

        await couponSvc.CreateCouponAsync(admin, "GANHE10", CouponDiscountType.FixedAmount, 10.00m, _clock.UtcNow.AddHours(-1), _clock.UtcNow.AddDays(1), minimumOrderAmount: 30m, maxTotalUses: 5, maxUsesPerCustomer: 1);

        // 1. Monta carrinho e aplica cupom
        await cartSvc.AddCatalogProductAsync("sess-ck-1", burger.Id, quantity: 2); // 2 x 32 = 64 (baixa 400g carne)
        await cartSvc.AddCustomAcaiCupAsync("sess-ck-1", new AcaiCupBuildRequest(cup500.Id, [], [new AcaiItemSelection(leite.Id)], [], 1, "Copo 1")); // 22
        await cartSvc.AddCustomAcaiCupAsync("sess-ck-1", new AcaiCupBuildRequest(cup500.Id, [], [new AcaiItemSelection(gran.Id)], [], 1, "Copo 2")); // 22
        await cartSvc.AddWeightedRecordAsync("sess-ck-1", weighRec.Id); // 20
        await cartSvc.ApplyCouponAsync("sess-ck-1", "GANHE10", "maria@cli.com");

        // 2. Cashier realiza Checkout (valida RBAC Orders -> Inventory -> Weighing -> Coupons -> Delivery -> SignalR notifier)
        var orderRes = await orderSvc.CheckoutCartAsync(cashier, new CheckoutCartRequest(
            "sess-ck-1",
            OrderChannel.OwnDeliveryMenu,
            FulfillmentMode.Delivery,
            "Maria Silva",
            "11999990000",
            DeliveryAddressSummary: "Av. Paulista, 1000",
            DestinationLatitude: -23.5874,
            DestinationLongitude: -46.6576,
            IdempotencyKey: "ck-1"));

        Assert.True(orderRes.IsSuccess);
        var order = orderRes.Value;
        Assert.Equal("#0001", order.OrderNumber);
        Assert.Equal(4, order.Items.Count); // 1 burger (qty 2) + 2 copos 500ml distintos + 1 pesagem
        // Subtotal: 64 + 22 + 22 + 20 = 128,00 - 10,00 cupom + 9,50 frete = 127,50
        Assert.Equal(127.50m, order.TotalAmount.Amount);

        // Estoque descontado: 5000g - 400g = 4600g
        var carneBal = await invDb.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == carne.Id);
        Assert.Equal(4600m, carneBal.QuantityOnHand);

        // Pesagem vinculada ao pedido
        var linkedWeigh = await weighDb.WeighingRecords.AsNoTracking().FirstAsync(w => w.Id == weighRec.Id);
        Assert.Equal(order.Id, linkedWeigh.LinkedOrderId);

        // Produto Burger agora tem referência histórica real: tentativa de excluir deve ARQUIVAR preservando histórico!
        var delOutcome = await catalogSvc.DeleteOrArchiveProductAsync(admin, burger.Id);
        Assert.Equal(DeletionOutcome.ArchivedPreservingHistory, delOutcome.Value);

        // Fluxo Kanban: Cozinha avança até pronto e entrega conclui
        await orderSvc.TransitionKanbanStatusAsync(admin, order.Id, OrderStatus.Accepted, "Aceito");
        await orderSvc.TransitionKanbanStatusAsync(kitchen, order.Id, OrderStatus.InPreparation, "Na chapa");
        await orderSvc.TransitionKanbanStatusAsync(kitchen, order.Id, OrderStatus.Ready, "Pronto");
        await orderSvc.TransitionKanbanStatusAsync(admin, order.Id, OrderStatus.Dispatched, "Saiu com entregador");
        await orderSvc.TransitionKanbanStatusAsync(admin, order.Id, OrderStatus.Completed, "Entregue");

        var boardAfterComplete = (await orderSvc.GetKanbanBoardAsync(admin)).Value;
        Assert.Empty(boardAfterComplete.Received);
        Assert.Empty(boardAfterComplete.Ready);
        Assert.Contains(notifier.Events, e => e.EventType == "OrderCreated" && e.OrderId == order.Id);

        // 3. Novo pedido para testar cancelamento com estorno de estoque e cupom
        var burger2 = (await catalogSvc.CreateProductAsync(admin, cat.Id, "Pitako Duplo", 40.00m)).Value;
        await supplySvc.CreateTechnicalSheetAsync(admin, "Ficha Duplo", 1m, "unidade", [new IngredientInput(carne.Id, 300m, "g")], [], productId: burger2.Id);

        await cartSvc.AddCatalogProductAsync("sess-ck-2", burger2.Id, quantity: 1);
        await cartSvc.ApplyCouponAsync("sess-ck-2", "GANHE10", "joao@cli.com");

        var order2 = (await orderSvc.CheckoutCartAsync(cashier, new CheckoutCartRequest(
            "sess-ck-2", OrderChannel.PdvCounter, FulfillmentMode.CounterPickup, "João"))).Value;

        Assert.Equal(4300m, (await invDb.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == carne.Id)).QuantityOnHand);

        var cancelRes = await orderSvc.TransitionKanbanStatusAsync(admin, order2.Id, OrderStatus.Cancelled, "Cliente desistiu no balcão");
        Assert.True(cancelRes.IsSuccess);
        Assert.Equal(4600m, (await invDb.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == carne.Id)).QuantityOnHand);

        // Cupom de João foi estornado e pode ser reutilizado
        var couponAfterCancel = await coupDb.Coupons.Include(c => c.Redemptions).AsNoTracking().FirstAsync(c => c.Code == "GANHE10");
        Assert.Single(couponAfterCancel.Redemptions, r => r.OrderId == order2.Id && r.IsReversed);
        Assert.Contains(notifier.Events, e => e.EventType == "OrderCancelled" && e.OrderId == order2.Id);
    }
}
