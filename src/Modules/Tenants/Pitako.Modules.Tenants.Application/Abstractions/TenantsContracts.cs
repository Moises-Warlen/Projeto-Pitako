using Pitako.Modules.Tenants.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Application.Abstractions;

public interface ITenantRepository
{
    Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<int> CountByCustomerAccountAsync(Guid customerAccountId, CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task RemoveWithChildrenAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStoreUnitRepository
{
    Task<StoreUnit?> FindByIdAsync(Guid unitId, CancellationToken cancellationToken = default);
    Task<int> CountByCustomerAccountAsync(Guid customerAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreUnit>> ListCurrentTenantUnitsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(StoreUnit unit, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStoreVisualIdentityRepository
{
    Task<StoreVisualIdentity?> FindCurrentTenantIdentityAsync(CancellationToken cancellationToken = default);
    Task<StoreVisualIdentity?> FindByTenantIdForPublicViewAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(StoreVisualIdentity identity, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPlanQuotaPolicyChecker
{
    Task<Result> CheckCanCreateEstablishmentAsync(Guid customerAccountId, int currentCount, CancellationToken cancellationToken = default);
    Task<Result> CheckCanCreateUnitAsync(Guid customerAccountId, int currentCount, CancellationToken cancellationToken = default);
}
