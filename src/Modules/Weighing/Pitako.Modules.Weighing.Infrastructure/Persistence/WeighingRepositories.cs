using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Domain;

namespace Pitako.Modules.Weighing.Infrastructure.Persistence;

public sealed class EfScaleDeviceRepository(WeighingDbContext db) : IScaleDeviceRepository
{
    public Task<ScaleDeviceConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ScaleDevices.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScaleDeviceConfig>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.ScaleDevices.AsNoTracking().Where(s => s.IsActive).OrderByDescending(s => s.IsDefault).ToListAsync(cancellationToken);

    public async Task AddAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default) =>
        await db.ScaleDevices.AddAsync(scale, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTarePresetRepository(WeighingDbContext db) : ITarePresetRepository
{
    public Task<TarePreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TarePresets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TarePreset>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.TarePresets.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(TarePreset tarePreset, CancellationToken cancellationToken = default) =>
        await db.TarePresets.AddAsync(tarePreset, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfWeighingRecordRepository(WeighingDbContext db) : IWeighingRecordRepository
{
    public Task<WeighingRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WeighingRecords.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddAsync(WeighingRecord record, CancellationToken cancellationToken = default) =>
        await db.WeighingRecords.AddAsync(record, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
