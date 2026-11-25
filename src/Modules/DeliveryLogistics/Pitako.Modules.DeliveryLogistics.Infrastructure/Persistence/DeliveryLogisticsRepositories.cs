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
