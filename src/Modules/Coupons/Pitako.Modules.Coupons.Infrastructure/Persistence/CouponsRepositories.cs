using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Domain;

namespace Pitako.Modules.Coupons.Infrastructure.Persistence;

public sealed class EfCouponRepository(CouponsDbContext db) : ICouponRepository
{
    private IQueryable<Coupon> FullQuery() =>
        db.Coupons
            .Include(c => c.EligibilityRules)
            .Include(c => c.Redemptions);

    public Task<Coupon?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Coupon?> FindByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(c => c.Code == normalizedCode, cancellationToken);

    public async Task<IReadOnlyList<Coupon>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await FullQuery().AsNoTracking().OrderBy(c => c.Code).ToListAsync(cancellationToken);

    public async Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default) =>
        await db.Coupons.AddAsync(coupon, cancellationToken);

    public void Remove(Coupon coupon) => db.Coupons.Remove(coupon);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
