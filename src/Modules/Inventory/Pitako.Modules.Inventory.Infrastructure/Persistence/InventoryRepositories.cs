using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Domain;

namespace Pitako.Modules.Inventory.Infrastructure.Persistence;

public sealed class EfTenantCustomUnitRepository(InventoryDbContext db) : ITenantCustomUnitRepository
{
    public async Task<IReadOnlyDictionary<string, UnitDefinition>> GetLookupAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.CustomUnits.AsNoTracking().ToListAsync(cancellationToken);
        return items.ToDictionary(u => u.Code, u => u.ToDefinition(), StringComparer.Ordinal);
    }

    public async Task AddAsync(TenantCustomUnit unit, CancellationToken cancellationToken = default) =>
        await db.CustomUnits.AddAsync(unit, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfSupplyItemRepository(InventoryDbContext db) : ISupplyItemRepository
{
    public Task<SupplyItem?> FindByIdAsync(Guid supplyItemId, CancellationToken cancellationToken = default) =>
        db.SupplyItems
            .Include(s => s.PriceHistory)
            .FirstOrDefaultAsync(s => s.Id == supplyItemId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, SupplyItem>> FindLookupByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<Guid, SupplyItem>();

        var items = await db.SupplyItems
            .Include(s => s.PriceHistory)
            .Where(s => distinctIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(s => s.Id);
    }

    public async Task AddAsync(SupplyItem item, CancellationToken cancellationToken = default) =>
        await db.SupplyItems.AddAsync(item, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTechnicalSheetRepository(InventoryDbContext db) : ITechnicalSheetRepository
{
    private IQueryable<TechnicalSheet> FullQuery() =>
        db.TechnicalSheets
            .Include(t => t.Ingredients)
            .Include(t => t.Packagings)
            .Include(t => t.CostHistory);

    public Task<TechnicalSheet?> FindByIdAsync(Guid sheetId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(t => t.Id == sheetId, cancellationToken);

    public async Task<IReadOnlyList<TechnicalSheet>> ListUsingSupplyItemAsync(
        Guid supplyItemId,
        CancellationToken cancellationToken = default) =>
        await FullQuery()
            .Where(t => t.IsActive &&
                        (t.Ingredients.Any(i => i.SupplyItemId == supplyItemId) ||
                         t.Packagings.Any(p => p.SupplyItemId == supplyItemId)))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(TechnicalSheet sheet, CancellationToken cancellationToken = default) =>
        await db.TechnicalSheets.AddAsync(sheet, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
