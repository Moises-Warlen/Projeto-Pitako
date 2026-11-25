using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Domain;

namespace Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;

public sealed class EfAcaiCupSizeRepository(AcaiBuilderDbContext db) : IAcaiCupSizeRepository
{
    public Task<AcaiCupSize?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CupSizes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AcaiCupSize>> ListAsync(bool includePaused, CancellationToken cancellationToken = default) =>
        await db.CupSizes
            .AsNoTracking()
            .Where(c => includePaused || c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.VolumeMl)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AcaiCupSize cupSize, CancellationToken cancellationToken = default) =>
        await db.CupSizes.AddAsync(cupSize, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfAcaiBaseFlavorRepository(AcaiBuilderDbContext db) : IAcaiBaseFlavorRepository
{
    public async Task<IReadOnlyDictionary<Guid, AcaiBaseFlavor>> FindLookupByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<Guid, AcaiBaseFlavor>();

        var items = await db.BaseFlavors
            .AsNoTracking()
            .Where(f => distinctIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(f => f.Id);
    }

    public async Task<IReadOnlyList<AcaiBaseFlavor>> ListAsync(bool includePaused, CancellationToken cancellationToken = default) =>
        await db.BaseFlavors
            .AsNoTracking()
            .Where(f => includePaused || f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AcaiBaseFlavor flavor, CancellationToken cancellationToken = default) =>
        await db.BaseFlavors.AddAsync(flavor, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfAcaiFreeToppingRepository(AcaiBuilderDbContext db) : IAcaiFreeToppingRepository
{
    public Task<AcaiFreeTopping?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.FreeToppings.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AcaiFreeTopping>> FindLookupByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<Guid, AcaiFreeTopping>();

        var items = await db.FreeToppings
            .AsNoTracking()
            .Where(t => distinctIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(t => t.Id);
    }

    public async Task<IReadOnlyList<AcaiFreeTopping>> ListAsync(bool includePaused, CancellationToken cancellationToken = default) =>
        await db.FreeToppings
            .AsNoTracking()
            .Where(t => includePaused || t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AcaiFreeTopping topping, CancellationToken cancellationToken = default) =>
        await db.FreeToppings.AddAsync(topping, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfAcaiPaidAddonRepository(AcaiBuilderDbContext db) : IAcaiPaidAddonRepository
{
    public Task<AcaiPaidAddon?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PaidAddons.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AcaiPaidAddon>> FindLookupByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<Guid, AcaiPaidAddon>();

        var items = await db.PaidAddons
            .AsNoTracking()
            .Where(a => distinctIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        return items.ToDictionary(a => a.Id);
    }

    public async Task<IReadOnlyList<AcaiPaidAddon>> ListAsync(bool includePaused, CancellationToken cancellationToken = default) =>
        await db.PaidAddons
            .AsNoTracking()
            .Where(a => includePaused || a.IsActive)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AcaiPaidAddon addon, CancellationToken cancellationToken = default) =>
        await db.PaidAddons.AddAsync(addon, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
