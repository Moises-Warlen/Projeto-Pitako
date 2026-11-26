using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Domain;

namespace Pitako.Modules.Marketplaces.Infrastructure.Persistence;

public sealed class EfMarketplacePartnerAccountRepository(MarketplacesDbContext db) : IMarketplacePartnerAccountRepository
{
    public Task<MarketplacePartnerAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PartnerAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MarketplacePartnerAccount>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.PartnerAccounts.AsNoTracking().OrderBy(a => a.AccountLabel).ToListAsync(cancellationToken);

    public async Task AddAsync(MarketplacePartnerAccount account, CancellationToken cancellationToken = default) =>
        await db.PartnerAccounts.AddAsync(account, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfMarketplaceStoreBindingRepository(MarketplacesDbContext db) : IMarketplaceStoreBindingRepository
{
    public Task<MarketplaceStoreBinding?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.StoreBindings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<MarketplaceStoreBinding?> FindByProviderAndMerchantAsync(
        MarketplaceProvider provider,
        string externalMerchantId,
        CancellationToken cancellationToken = default) =>
        db.StoreBindings.FirstOrDefaultAsync(b => b.Provider == provider && b.ExternalMerchantId == externalMerchantId, cancellationToken);

    public async Task<IReadOnlyList<MarketplaceStoreBinding>> ListByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idSet = ids.Distinct().ToList();
        return await db.StoreBindings.Where(b => idSet.Contains(b.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketplaceStoreBinding>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.StoreBindings.AsNoTracking().OrderBy(b => b.ExternalStoreName).ToListAsync(cancellationToken);

    public async Task AddAsync(MarketplaceStoreBinding binding, CancellationToken cancellationToken = default) =>
        await db.StoreBindings.AddAsync(binding, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfMarketplaceInboundEventRepository(MarketplacesDbContext db) : IMarketplaceInboundEventRepository
{
    public Task<MarketplaceInboundEvent?> FindByExternalEventIdAsync(
        MarketplaceProvider provider,
        string externalEventId,
        CancellationToken cancellationToken = default) =>
        db.InboundEvents.FirstOrDefaultAsync(e => e.Provider == provider && e.ExternalEventId == externalEventId, cancellationToken);

    public async Task<IReadOnlyList<MarketplaceInboundEvent>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
            return await db.InboundEvents.AsNoTracking().OrderByDescending(e => e.ReceivedAtUtc).Take(limit).ToListAsync(cancellationToken);

        var list = await db.InboundEvents.AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(e => e.ReceivedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(MarketplaceInboundEvent inboundEvent, CancellationToken cancellationToken = default) =>
        await db.InboundEvents.AddAsync(inboundEvent, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfMarketplaceCatalogMappingRepository(MarketplacesDbContext db) : IMarketplaceCatalogMappingRepository
{
    public Task<MarketplaceCatalogMapping?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CatalogMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MarketplaceCatalogMapping>> ListByBindingAsync(Guid storeBindingId, CancellationToken cancellationToken = default) =>
        await db.CatalogMappings.Where(m => m.StoreBindingId == storeBindingId).OrderBy(m => m.ExternalSkuCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MarketplaceCatalogMapping>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.CatalogMappings.AsNoTracking().OrderBy(m => m.ExternalSkuCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MarketplaceCatalogMapping>> ListByInternalProductAsync(Guid internalProductId, CancellationToken cancellationToken = default) =>
        await db.CatalogMappings.Where(m => m.InternalProductId == internalProductId).ToListAsync(cancellationToken);

    public async Task AddAsync(MarketplaceCatalogMapping mapping, CancellationToken cancellationToken = default) =>
        await db.CatalogMappings.AddAsync(mapping, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfMarketplaceBulkJobRepository(MarketplacesDbContext db) : IMarketplaceBulkJobRepository
{
    public Task<MarketplaceBulkJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.BulkJobs.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MarketplaceBulkJob>> ListRecentAsync(int limit = 30, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.BulkJobs
                .Include(j => j.Items)
                .AsNoTracking()
                .OrderByDescending(j => j.StartedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        var list = await db.BulkJobs.Include(j => j.Items).AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(j => j.StartedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(MarketplaceBulkJob job, CancellationToken cancellationToken = default) =>
        await db.BulkJobs.AddAsync(job, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
