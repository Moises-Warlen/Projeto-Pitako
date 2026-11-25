using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;

namespace Pitako.Modules.Tenants.Infrastructure;

public sealed class EfTenantRepository(TenantsDbContext db) : ITenantRepository
{
    public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug.Trim().ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Tenants.OrderBy(t => t.TradeName).ToListAsync(cancellationToken);

    public Task<int> CountByCustomerAccountAsync(Guid customerAccountId, CancellationToken cancellationToken = default) =>
        db.Tenants.CountAsync(t => t.CustomerAccountId == customerAccountId && t.Status != TenantStatus.Cancelled, cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default) =>
        await db.Tenants.AddAsync(tenant, cancellationToken);

    public async Task RemoveWithChildrenAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        var units = await db.StoreUnits.IgnoreQueryFilters().Where(u => u.TenantId == tenant.Id).ToListAsync(cancellationToken);
        var identities = await db.StoreVisualIdentities.IgnoreQueryFilters().Include(i => i.Banners).Where(i => i.TenantId == tenant.Id).ToListAsync(cancellationToken);
        if (units.Count > 0) db.StoreUnits.RemoveRange(units);
        if (identities.Count > 0) db.StoreVisualIdentities.RemoveRange(identities);
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfStoreUnitRepository(TenantsDbContext db) : IStoreUnitRepository
{
    public Task<StoreUnit?> FindByIdAsync(Guid unitId, CancellationToken cancellationToken = default) =>
        db.StoreUnits.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);

    public async Task<int> CountByCustomerAccountAsync(Guid customerAccountId, CancellationToken cancellationToken = default)
    {
        var tenantIds = await db.Tenants
            .Where(t => t.CustomerAccountId == customerAccountId && t.Status != TenantStatus.Cancelled)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
            return 0;

        return await db.StoreUnits
            .IgnoreQueryFilters()
            .CountAsync(u => tenantIds.Contains(u.TenantId) && u.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<StoreUnit>> ListCurrentTenantUnitsAsync(CancellationToken cancellationToken = default) =>
        await db.StoreUnits.OrderBy(u => u.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(StoreUnit unit, CancellationToken cancellationToken = default) =>
        await db.StoreUnits.AddAsync(unit, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfStoreVisualIdentityRepository(TenantsDbContext db) : IStoreVisualIdentityRepository
{
    public Task<StoreVisualIdentity?> FindCurrentTenantIdentityAsync(CancellationToken cancellationToken = default) =>
        db.StoreVisualIdentities
            .Include(i => i.Banners)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StoreVisualIdentity?> FindByTenantIdForPublicViewAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        db.StoreVisualIdentities
            .IgnoreQueryFilters()
            .Include(i => i.Banners)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(StoreVisualIdentity identity, CancellationToken cancellationToken = default) =>
        await db.StoreVisualIdentities.AddAsync(identity, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
