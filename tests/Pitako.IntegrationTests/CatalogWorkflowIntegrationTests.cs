using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class StubHistoryReferenceChecker(HashSet<Guid> productsWithHistory) : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(productsWithHistory.Contains(productId));
}

public sealed class CatalogWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 24, 22, 35, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private CatalogDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task Catalog_FullLifecycle_ShouldPersistCategoriesProductsModifiersCombosPromotionsAndHistoryRules()
    {
        var tenantId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var productsWithHistory = new HashSet<Guid>();
        var historyChecker = new StubHistoryReferenceChecker(productsWithHistory);

        Guid productWithHistoryId;
        Guid freshProductId;

        await using (var db = CreateDb(ctx))
        {
            var svc = new CatalogManagementService(
                new EfCategoryRepository(db),
                new EfProductRepository(db),
                new EfModifierGroupRepository(db),
                new EfComboRepository(db),
                new EfPromotionRepository(db),
                historyChecker,
                ctx,
                _clock);

            var cat = (await svc.CreateCategoryAsync(admin, "Açaís Tradicionais", "Copos montados", null, 1)).Value;
            var p1 = (await svc.CreateProductAsync(admin, cat.Id, "Açaí Clássico", 22.00m, "Puro", "ACAI-1", ProductSaleMode.CustomizableAcai, true, 1)).Value;
            var p2 = (await svc.CreateProductAsync(admin, cat.Id, "Água Mineral", 5.00m, "500ml", "AGUA-1", ProductSaleMode.Unit, false, 2)).Value;
            productWithHistoryId = p1.Id;
            freshProductId = p2.Id;

            await svc.AddProductImageAsync(admin, p1.Id, "https://cdn.loja.com/acai1.jpg", 1, isPrimary: true);
            await svc.AddProductVariationAsync(admin, p1.Id, "500 ml", 22.00m, "ACAI-500", 1);

            var group = (await svc.CreateModifierGroupAsync(admin, "Extras", ModifierGroupType.Addon, 0, 3, false,
                [("Nutella", 6.00m, 2), ("Morango Extra", 4.50m, 3)])).Value;
            await svc.AttachModifierGroupToProductAsync(admin, p1.Id, group.Id, 1);

            var combo = await svc.CreateComboAsync(admin, "Combo Refrescante", 24.00m, [(p1.Id, 1, null, null), (p2.Id, 1, null, null)]);
            Assert.True(combo.IsSuccess);

            var promo = await svc.CreatePromotionAsync(admin, "Desconto 10%", PromotionDiscountType.Percentage, 10m,
                _clock.UtcNow.AddMinutes(-10), _clock.UtcNow.AddHours(2), [(PromotionTargetType.Product, p1.Id)]);
            Assert.True(promo.IsSuccess);

            // Preço efetivo reflete promoção ativa
            var effective = await svc.GetEffectiveProductPriceAsync(p1.Id);
            Assert.True(effective.IsSuccess);
            Assert.Equal(22.00m, effective.Value.OriginalPrice.Amount);
            Assert.Equal(19.80m, effective.Value.FinalPrice.Amount);
            Assert.True(effective.Value.CanBeOrdered);

            // Pausa impede novos pedidos
            await svc.PauseProductAsync(admin, p1.Id);
            var whilePaused = await svc.GetEffectiveProductPriceAsync(p1.Id);
            Assert.False(whilePaused.Value.CanBeOrdered);

            await svc.ReactivateProductAsync(admin, p1.Id);
        }

        // Testa exclusão com histórico (arquiva) vs sem histórico (exclui fisicamente)
        productsWithHistory.Add(productWithHistoryId);
        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new CatalogManagementService(
                new EfCategoryRepository(db2),
                new EfProductRepository(db2),
                new EfModifierGroupRepository(db2),
                new EfComboRepository(db2),
                new EfPromotionRepository(db2),
                historyChecker,
                ctx,
                _clock);

            var deleteHistoryRes = await svc2.DeleteOrArchiveProductAsync(admin, productWithHistoryId);
            Assert.True(deleteHistoryRes.IsSuccess);
            Assert.Equal(DeletionOutcome.ArchivedPreservingHistory, deleteHistoryRes.Value);

            var archived = await db2.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productWithHistoryId);
            Assert.NotNull(archived);
            Assert.Equal(CatalogItemStatus.Archived, archived!.Status);

            var deleteFreshRes = await svc2.DeleteOrArchiveProductAsync(admin, freshProductId);
            Assert.True(deleteFreshRes.IsSuccess);
            Assert.Equal(DeletionOutcome.PhysicallyDeleted, deleteFreshRes.Value);

            var gone = await db2.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == freshProductId);
            Assert.Null(gone);
        }
    }
}
