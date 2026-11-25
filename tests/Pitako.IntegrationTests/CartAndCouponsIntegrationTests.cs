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
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Infrastructure.Hardware;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class StubNoHistoryChecker : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class CartAndCouponsIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _catalogConn = new("Data Source=:memory:");
    private readonly SqliteConnection _acaiConn = new("Data Source=:memory:");
    private readonly SqliteConnection _weighingConn = new("Data Source=:memory:");
    private readonly SqliteConnection _couponsConn = new("Data Source=:memory:");
    private readonly SqliteConnection _ordersConn = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 15, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        var initCtx = new TenantContext();
        initCtx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);

        foreach (var conn in new[] { _catalogConn, _acaiConn, _weighingConn, _couponsConn, _ordersConn })
            await conn.OpenAsync();

        await using var catDb = CreateCatalogDb(initCtx); await catDb.Database.EnsureCreatedAsync();
        await using var acaiDb = CreateAcaiDb(initCtx); await acaiDb.Database.EnsureCreatedAsync();
        await using var weighDb = CreateWeighingDb(initCtx); await weighDb.Database.EnsureCreatedAsync();
        await using var coupDb = CreateCouponsDb(initCtx); await coupDb.Database.EnsureCreatedAsync();
        await using var ordDb = CreateOrdersDb(initCtx); await ordDb.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _catalogConn.DisposeAsync();
        await _acaiConn.DisposeAsync();
        await _weighingConn.DisposeAsync();
        await _couponsConn.DisposeAsync();
        await _ordersConn.DisposeAsync();
    }

    private CatalogDbContext CreateCatalogDb(ITenantContext ctx) => new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, ctx, _clock);
    private AcaiBuilderDbContext CreateAcaiDb(ITenantContext ctx) => new(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctx, _clock);
    private WeighingDbContext CreateWeighingDb(ITenantContext ctx) => new(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighingConn).Options, ctx, _clock);
    private CouponsDbContext CreateCouponsDb(ITenantContext ctx) => new(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponsConn).Options, ctx, _clock);
    private OrdersDbContext CreateOrdersDb(ITenantContext ctx) => new(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, ctx, _clock);

    [Fact]
    public async Task Cart_ShouldPreserveCustomizedAcaiCupsSeparately_RecalculateBackendPrices_AndApplyCoupons()
    {
        var tenantId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        await using var catDb = CreateCatalogDb(ctx);
        await using var acaiDb = CreateAcaiDb(ctx);
        await using var weighDb = CreateWeighingDb(ctx);
        await using var coupDb = CreateCouponsDb(ctx);
        await using var ordDb = CreateOrdersDb(ctx);

        var catalogSvc = new CatalogManagementService(
            new EfCategoryRepository(catDb),
            new EfProductRepository(catDb),
            new EfModifierGroupRepository(catDb),
            new EfComboRepository(catDb),
            new EfPromotionRepository(catDb),
            new StubNoHistoryChecker(),
            ctx,
            _clock);

        var acaiSvc = new AcaiBuilderManagementService(
            new EfAcaiCupSizeRepository(acaiDb),
            new EfAcaiBaseFlavorRepository(acaiDb),
            new EfAcaiFreeToppingRepository(acaiDb),
            new EfAcaiPaidAddonRepository(acaiDb),
            ctx);

        var weighingSvc = new WeighingService(
            new EfScaleDeviceRepository(weighDb),
            new EfTarePresetRepository(weighDb),
            new EfWeighingRecordRepository(weighDb),
            new UnavailablePhysicalScaleTransport(),
            ctx,
            _clock);

        var couponSvc = new CouponsManagementService(new EfCouponRepository(coupDb), ctx, _clock);

        var cartSvc = new ShoppingCartService(
            new EfShoppingCartRepository(ordDb),
            catalogSvc,
            acaiSvc,
            weighingSvc,
            couponSvc,
            ctx,
            _clock);

        // 1. Setup de produtos de catálogo, copo de açaí 500ml, pesagem e cupom
        var catBurgers = (await catalogSvc.CreateCategoryAsync(admin, "Lanches")).Value;
        var burger = (await catalogSvc.CreateProductAsync(admin, catBurgers.Id, "Pitako Burger", 30.00m)).Value;
        var extrasGroup = (await catalogSvc.CreateModifierGroupAsync(admin, "Adicionais", ModifierGroupType.Addon, 0, 2, false, [("Bacon", 5.00m, 2)])).Value;
        await catalogSvc.AttachModifierGroupToProductAsync(admin, burger.Id, extrasGroup.Id);

        var cup500 = (await acaiSvc.CreateCupSizeAsync(admin, "500 ml", 500, 22.00m, 5)).Value;
        var leite = (await acaiSvc.CreateFreeToppingAsync(admin, "Leite em pó", 1)).Value;
        var granola = (await acaiSvc.CreateFreeToppingAsync(admin, "Granola", 1)).Value;
        var nutella = (await acaiSvc.CreatePaidAddonAsync(admin, "Nutella", 6.00m, 2)).Value;

        var weighRec = (await weighingSvc.RegisterManualWeighingAsync(admin, "Açaí Self-Service", 50.00m, 420m, "Balança offline", manualTareGrams: 20m)).Value; // 400g = R$ 20,00

        await couponSvc.CreateCouponAsync(
            admin,
            "BEMVINDO10",
            CouponDiscountType.Percentage,
            10m,
            _clock.UtcNow.AddHours(-1),
            _clock.UtcNow.AddDays(5),
            minimumOrderAmount: 50.00m,
            maxDiscountAmount: 15.00m,
            maxTotalUses: 100,
            maxUsesPerCustomer: 1);

        // 2. Monta o carrinho adicionando 2 copos de 500 ml com escolhas diferentes + burger c/ bacon + item pesado
        const string sessionKey = "web-sess-001";
        await cartSvc.AddCustomAcaiCupAsync(sessionKey, new AcaiCupBuildRequest(
            cup500.Id, [], [new AcaiItemSelection(leite.Id)], [new AcaiItemSelection(nutella.Id)], 1, "Copo 1")); // R$ 28,00
        await cartSvc.AddCustomAcaiCupAsync(sessionKey, new AcaiCupBuildRequest(
            cup500.Id, [], [new AcaiItemSelection(granola.Id)], [], 1, "Copo 2")); // R$ 22,00
        await cartSvc.AddCatalogProductAsync(sessionKey, burger.Id, null, [new CatalogModifierSelection(extrasGroup.Id, extrasGroup.Options.First().Id, 1)], 1); // R$ 35,00
        await cartSvc.AddWeightedRecordAsync(sessionKey, weighRec.Id); // R$ 20,00
        await cartSvc.SetDeliveryFeeAsync(sessionKey, 8.00m);

        var cartWithCoupon = await cartSvc.ApplyCouponAsync(sessionKey, "bemvindo10", "joao@cliente.com");
        Assert.True(cartWithCoupon.IsSuccess);

        var cart = cartWithCoupon.Value;
        // 4 itens distintos preservados (2 copos 500ml distintos + 1 burger + 1 pesagem)
        Assert.Equal(4, cart.Items.Count);
        Assert.Equal(2, cart.Items.Count(i => i.AcaiCupSizeId == cup500.Id));
        // Subtotal: 28 + 22 + 35 + 20 = 105,00; Desconto 10% = 10,50 (abaixo do teto 15,00); Entrega = 8,00 -> Total = 102,50
        Assert.Equal(105.00m, cart.ItemsSubtotal.Amount);
        Assert.Equal(10.50m, cart.ItemsDiscount.Amount);
        Assert.Equal("BEMVINDO10", cart.AppliedCouponCode);
        Assert.Equal(102.50m, cart.TotalAmount.Amount);

        // Confirma persistência recarregando em nova instância de OrdersDbContext
        await using var ordDbReload = CreateOrdersDb(ctx);
        var reloadedCart = await ordDbReload.ShoppingCarts.Include(c => c.Items).AsNoTracking().FirstAsync(c => c.CartSessionKey == sessionKey);
        Assert.Equal(4, reloadedCart.Items.Count);
        Assert.Equal(102.50m, reloadedCart.TotalAmount.Amount);
    }
}
