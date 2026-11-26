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

    public async Task<IReadOnlyList<SupplyItem>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.SupplyItems.OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(SupplyItem item, CancellationToken cancellationToken = default) =>
        await db.SupplyItems.AddAsync(item, cancellationToken);

    public void Remove(SupplyItem item) => db.SupplyItems.Remove(item);

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

    public Task<TechnicalSheet?> FindActiveByProductIdAsync(Guid productId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(t => t.IsActive && t.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<TechnicalSheet>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await FullQuery().OrderBy(t => t.Name).ToListAsync(cancellationToken);

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

    public void Remove(TechnicalSheet sheet) => db.TechnicalSheets.Remove(sheet);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfStockBalanceRepository(InventoryDbContext db) : IStockBalanceRepository
{
    private IQueryable<StockBalance> FullQuery() =>
        db.StockBalances
            .Include(b => b.Lots)
            .Include(b => b.Movements);

    public Task<StockBalance?> FindBySupplyItemIdAsync(Guid supplyItemId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(b => b.SupplyItemId == supplyItemId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, StockBalance>> FindLookupBySupplyItemIdsAsync(
        IEnumerable<Guid> supplyItemIds,
        CancellationToken cancellationToken = default)
    {
        var ids = supplyItemIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, StockBalance>();

        var items = await FullQuery()
            .Where(b => ids.Contains(b.SupplyItemId))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(b => b.SupplyItemId);
    }

    public async Task<IReadOnlyList<StockBalance>> ListAllWithLotsAsync(CancellationToken cancellationToken = default) =>
        await db.StockBalances
            .Include(b => b.Lots)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockMovement>> ListByReferenceAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default) =>
        await db.StockMovements
            .AsNoTracking()
            .Where(m => m.ReferenceType == referenceType && m.ReferenceId == referenceId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(StockBalance balance, CancellationToken cancellationToken = default) =>
        await db.StockBalances.AddAsync(balance, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPurchaseReceiptRepository(InventoryDbContext db) : IPurchaseReceiptRepository
{
    private IQueryable<PurchaseReceipt> FullQuery() =>
        db.PurchaseReceipts.Include(r => r.Items);

    public Task<PurchaseReceipt?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

    public Task<PurchaseReceipt?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task AddAsync(PurchaseReceipt receipt, CancellationToken cancellationToken = default) =>
        await db.PurchaseReceipts.AddAsync(receipt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfProductionOrderRepository(InventoryDbContext db) : IProductionOrderRepository
{
    public Task<ProductionOrder?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.ProductionOrders
            .Include(o => o.Consumptions)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default) =>
        await db.ProductionOrders.AddAsync(order, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
