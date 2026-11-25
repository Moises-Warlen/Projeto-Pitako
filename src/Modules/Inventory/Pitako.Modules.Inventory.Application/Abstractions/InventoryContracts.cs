using Pitako.Modules.Inventory.Domain;

namespace Pitako.Modules.Inventory.Application.Abstractions;

public interface ITenantCustomUnitRepository
{
    Task<IReadOnlyDictionary<string, UnitDefinition>> GetLookupAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TenantCustomUnit unit, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISupplyItemRepository
{
    Task<SupplyItem?> FindByIdAsync(Guid supplyItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, SupplyItem>> FindLookupByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AddAsync(SupplyItem item, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITechnicalSheetRepository
{
    Task<TechnicalSheet?> FindByIdAsync(Guid sheetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TechnicalSheet>> ListUsingSupplyItemAsync(Guid supplyItemId, CancellationToken cancellationToken = default);
    Task AddAsync(TechnicalSheet sheet, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
