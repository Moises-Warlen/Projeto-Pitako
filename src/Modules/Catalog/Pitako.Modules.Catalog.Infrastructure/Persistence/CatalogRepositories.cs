using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Domain;

namespace Pitako.Modules.Catalog.Infrastructure.Persistence;

public sealed class EfCategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public Task<Category?> FindByIdAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListOrderedAsync(CancellationToken cancellationToken = default) =>
        await db.Categories.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default) =>
        await db.Categories.AddAsync(category, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfProductRepository(CatalogDbContext db) : IProductRepository
{
    private IQueryable<Product> FullQuery() =>
        db.Products
            .Include(p => p.Images)
            .Include(p => p.Variations)
            .Include(p => p.ModifierLinks);

    public Task<Product?> FindByIdAsync(Guid productId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await FullQuery().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListAllOrderedAsync(CancellationToken cancellationToken = default) =>
        await FullQuery().OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default) =>
        await db.Products.AddAsync(product, cancellationToken);

    public void Remove(Product product) => db.Products.Remove(product);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfModifierGroupRepository(CatalogDbContext db) : IModifierGroupRepository
{
    public Task<ModifierGroup?> FindByIdAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        db.ModifierGroups.Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

    public async Task<IReadOnlyList<ModifierGroup>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.ModifierGroups.Include(g => g.Options).OrderBy(g => g.DisplayOrder).ToListAsync(cancellationToken);

    public async Task AddAsync(ModifierGroup group, CancellationToken cancellationToken = default) =>
        await db.ModifierGroups.AddAsync(group, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfComboRepository(CatalogDbContext db) : IComboRepository
{
    public Task<Combo?> FindByIdAsync(Guid comboId, CancellationToken cancellationToken = default) =>
        db.Combos.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == comboId, cancellationToken);

    public async Task<IReadOnlyList<Combo>> ListAllOrderedAsync(CancellationToken cancellationToken = default) =>
        await db.Combos.Include(c => c.Items).OrderBy(c => c.DisplayOrder).ToListAsync(cancellationToken);

    public async Task AddAsync(Combo combo, CancellationToken cancellationToken = default) =>
        await db.Combos.AddAsync(combo, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPromotionRepository(CatalogDbContext db) : IPromotionRepository
{
    public Task<Promotion?> FindByIdAsync(Guid promotionId, CancellationToken cancellationToken = default) =>
        db.Promotions.Include(p => p.Targets).FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);

    public async Task<IReadOnlyList<Promotion>> ListActiveCandidatesAsync(CancellationToken cancellationToken = default) =>
        await db.Promotions.Include(p => p.Targets).Where(p => p.IsActive).ToListAsync(cancellationToken);

    public async Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default) =>
        await db.Promotions.AddAsync(promotion, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
