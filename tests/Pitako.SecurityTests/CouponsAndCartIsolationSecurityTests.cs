using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
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
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecurityNoHistoryChecker : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class CouponsAndCartIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _catalogConn = new("Data Source=:memory:");
    private readonly SqliteConnection _acaiConn = new("Data Source=:memory:");
    private readonly SqliteConnection _weighingConn = new("Data Source=:memory:");
    private readonly SqliteConnection _couponsConn = new("Data Source=:memory:");
    private readonly SqliteConnection _ordersConn = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

    public async Task InitializeAsync()
    {
        var initCtx = new TenantContext();
        initCtx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);

        foreach (var conn in new[] { _catalogConn, _acaiConn, _weighingConn, _couponsConn, _ordersConn })
            await conn.OpenAsync();

        await using var c1 = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, initCtx, _clock);
        await c1.Database.EnsureCreatedAsync();
        await using var c2 = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, initCtx, _clock);
        await c2.Database.EnsureCreatedAsync();
        await using var c3 = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighingConn).Options, initCtx, _clock);
        await c3.Database.EnsureCreatedAsync();
        await using var c4 = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponsConn).Options, initCtx, _clock);
        await c4.Database.EnsureCreatedAsync();
        await using var c5 = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, initCtx, _clock);
        await c5.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _catalogConn.DisposeAsync();
        await _acaiConn.DisposeAsync();
        await _weighingConn.DisposeAsync();
        await _couponsConn.DisposeAsync();
        await _ordersConn.DisposeAsync();
    }

    [Fact]
    public async Task TenantA_ShouldNeverSeeUseOrMix_CouponsCartsAndProductsOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid prodBId;
        Guid couponBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var catB = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, ctxB, _clock))
        await using (var acaiB = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctxB, _clock))
        await using (var weighB = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighingConn).Options, ctxB, _clock))
        await using (var coupB = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponsConn).Options, ctxB, _clock))
        await using (var ordB = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, ctxB, _clock))
        {
            var catSvcB = new CatalogManagementService(new EfCategoryRepository(catB), new EfProductRepository(catB), new EfModifierGroupRepository(catB), new EfComboRepository(catB), new EfPromotionRepository(catB), new SecurityNoHistoryChecker(), ctxB, _clock);
            var acaiSvcB = new AcaiBuilderManagementService(new EfAcaiCupSizeRepository(acaiB), new EfAcaiBaseFlavorRepository(acaiB), new EfAcaiFreeToppingRepository(acaiB), new EfAcaiPaidAddonRepository(acaiB), ctxB);
            var weighSvcB = new WeighingService(new EfScaleDeviceRepository(weighB), new EfTarePresetRepository(weighB), new EfWeighingRecordRepository(weighB), new UnavailablePhysicalScaleTransport(), ctxB, _clock);
            var coupSvcB = new CouponsManagementService(new EfCouponRepository(coupB), ctxB, _clock);
            var cartSvcB = new ShoppingCartService(new EfShoppingCartRepository(ordB), catSvcB, acaiSvcB, weighSvcB, coupSvcB, ctxB, _clock);

            var categoryB = (await catSvcB.CreateCategoryAsync(adminB, "Cat B")).Value;
            prodBId = (await catSvcB.CreateProductAsync(adminB, categoryB.Id, "Pizza B", 50m)).Value.Id;
            couponBId = (await coupSvcB.CreateCouponAsync(adminB, "SECRETOB", CouponDiscountType.FixedAmount, 15m, _clock.UtcNow.AddHours(-1), _clock.UtcNow.AddDays(2))).Value.Id;
            await cartSvcB.AddCatalogProductAsync("sess-shared-name", prodBId);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var catA = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConn).Options, ctxA, _clock);
        await using var acaiA = new AcaiBuilderDbContext(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_acaiConn).Options, ctxA, _clock);
        await using var weighA = new WeighingDbContext(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_weighingConn).Options, ctxA, _clock);
        await using var coupA = new CouponsDbContext(new DbContextOptionsBuilder<CouponsDbContext>().UseSqlite(_couponsConn).Options, ctxA, _clock);
        await using var ordA = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConn).Options, ctxA, _clock);

        var catSvcA = new CatalogManagementService(new EfCategoryRepository(catA), new EfProductRepository(catA), new EfModifierGroupRepository(catA), new EfComboRepository(catA), new EfPromotionRepository(catA), new SecurityNoHistoryChecker(), ctxA, _clock);
        var acaiSvcA = new AcaiBuilderManagementService(new EfAcaiCupSizeRepository(acaiA), new EfAcaiBaseFlavorRepository(acaiA), new EfAcaiFreeToppingRepository(acaiA), new EfAcaiPaidAddonRepository(acaiA), ctxA);
        var weighSvcA = new WeighingService(new EfScaleDeviceRepository(weighA), new EfTarePresetRepository(weighA), new EfWeighingRecordRepository(weighA), new UnavailablePhysicalScaleTransport(), ctxA, _clock);
        var coupSvcA = new CouponsManagementService(new EfCouponRepository(coupA), ctxA, _clock);
        var cartSvcA = new ShoppingCartService(new EfShoppingCartRepository(ordA), catSvcA, acaiSvcA, weighSvcA, coupSvcA, ctxA, _clock);

        // Global query filter oculta cupons, carrinhos e itens de B
        Assert.Empty(await coupA.Coupons.AsNoTracking().ToListAsync());
        Assert.Empty(await ordA.ShoppingCarts.AsNoTracking().ToListAsync());
        Assert.Empty(await ordA.ShoppingCartItems.AsNoTracking().ToListAsync());

        // Loja A usando a mesma sessionKey "sess-shared-name" cria carrinho próprio vazio, sem enxergar item de B
        var cartA = (await cartSvcA.GetOrCreateCartAsync("sess-shared-name")).Value;
        Assert.Empty(cartA.Items);

        // Loja A não consegue adicionar produto de B nem usar cupom de B
        var addProdB = await cartSvcA.AddCatalogProductAsync("sess-shared-name", prodBId);
        Assert.True(addProdB.IsFailure);
        Assert.Equal("Product.NotFound", addProdB.Error.Code);

        var evalCouponB = await coupSvcA.EvaluateCouponAsync("SECRETOB", null, [new CouponBasketLine(null, null, Money.Create(100m).Value)], Money.Zero());
        Assert.True(evalCouponB.IsFailure);
        Assert.Equal("Coupon.NotFound", evalCouponB.Error.Code);

        var deactB = await coupSvcA.DeactivateCouponAsync(adminA, couponBId);
        Assert.True(deactB.IsFailure);
        Assert.Equal("Coupon.NotFound", deactB.Error.Code);

        // Menor privilégio: Cozinha não pode criar cupom
        var kitchenCoupon = await coupSvcA.CreateCouponAsync(kitchenA, "COZ10", CouponDiscountType.Percentage, 10m, _clock.UtcNow, _clock.UtcNow.AddDays(1));
        Assert.True(kitchenCoupon.IsFailure);
        Assert.Equal("Coupon.Forbidden", kitchenCoupon.Error.Code);
    }
}
