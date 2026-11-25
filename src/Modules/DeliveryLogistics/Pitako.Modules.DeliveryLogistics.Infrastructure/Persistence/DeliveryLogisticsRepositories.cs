using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Domain;

namespace Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;

public sealed class EfDeliveryPolicyRepository(DeliveryLogisticsDbContext db) : IDeliveryPolicyRepository
{
    private IQueryable<DeliveryPolicy> FullQuery() =>
        db.DeliveryPolicies
            .Include(p => p.DistanceTiers)
            .Include(p => p.ZoneRules);

    public Task<DeliveryPolicy?> FindByIdAsync(Guid policyId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);

    public async Task<DeliveryPolicy?> FindEffectivePolicyAsync(Guid? storeUnitId, CancellationToken cancellationToken = default)
    {
        if (storeUnitId.HasValue && storeUnitId.Value != Guid.Empty)
        {
            var unitPolicy = await FullQuery().FirstOrDefaultAsync(p => p.StoreUnitId == storeUnitId.Value, cancellationToken);
            if (unitPolicy is not null)
                return unitPolicy;
        }

        return await FullQuery()
            .OrderBy(p => p.StoreUnitId.HasValue ? 1 : 0)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(DeliveryPolicy policy, CancellationToken cancellationToken = default) =>
        await db.DeliveryPolicies.AddAsync(policy, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfDeliveryCourierRepository(DeliveryLogisticsDbContext db) : IDeliveryCourierRepository
{
    public Task<DeliveryCourier?> FindByIdAsync(Guid courierId, CancellationToken cancellationToken = default) =>
        db.Couriers.FirstOrDefaultAsync(c => c.Id == courierId, cancellationToken);

    public async Task<IReadOnlyList<DeliveryCourier>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Couriers.AsNoTracking().OrderBy(c => c.FullName).ToListAsync(cancellationToken);

    public async Task AddAsync(DeliveryCourier courier, CancellationToken cancellationToken = default) =>
        await db.Couriers.AddAsync(courier, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfDeliveryDispatchRunRepository(DeliveryLogisticsDbContext db) : IDeliveryDispatchRunRepository
{
    private IQueryable<DeliveryDispatchRun> FullQuery() =>
        db.DispatchRuns.Include(r => r.Stops);

    public Task<DeliveryDispatchRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.DispatchRuns.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<DeliveryDispatchRun>> ListByCourierPeriodAsync(
        Guid courierId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await FullQuery()
                .AsNoTracking()
                .Where(r => r.CourierId == courierId && r.AssignedAtUtc >= fromUtc && r.AssignedAtUtc <= toUtc)
                .OrderByDescending(r => r.AssignedAtUtc)
                .ToListAsync(cancellationToken);
        }

        var items = await FullQuery()
            .AsNoTracking()
            .Where(r => r.CourierId == courierId)
            .ToListAsync(cancellationToken);

        return items
            .Where(r => r.AssignedAtUtc >= fromUtc && r.AssignedAtUtc <= toUtc)
            .OrderByDescending(r => r.AssignedAtUtc)
            .ToList();
    }

    public async Task AddAsync(DeliveryDispatchRun run, CancellationToken cancellationToken = default) =>
        await db.DispatchRuns.AddAsync(run, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfCourierGpsPingRepository(DeliveryLogisticsDbContext db) : ICourierGpsPingRepository
{
    public async Task<IReadOnlyList<CourierGpsPing>> ListRecentByCourierAsync(
        Guid courierId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.GpsPings
                .AsNoTracking()
                .Where(p => p.CourierId == courierId)
                .OrderByDescending(p => p.RecordedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        var items = await db.GpsPings
            .AsNoTracking()
            .Where(p => p.CourierId == courierId)
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(p => p.RecordedAtUtc)
            .Take(limit)
            .ToList();
    }

    public async Task AddAsync(CourierGpsPing ping, CancellationToken cancellationToken = default) =>
        await db.GpsPings.AddAsync(ping, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
