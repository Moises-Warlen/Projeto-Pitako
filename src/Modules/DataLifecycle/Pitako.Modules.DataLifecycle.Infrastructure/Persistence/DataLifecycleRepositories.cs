using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Domain;

namespace Pitako.Modules.DataLifecycle.Infrastructure.Persistence;

public sealed class EfDataMigrationJobRepository(DataLifecycleDbContext db) : IDataMigrationJobRepository
{
    public Task<DataMigrationJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MigrationJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DataMigrationJob>> ListRecentAsync(int limit = 30, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
            return await db.MigrationJobs.AsNoTracking().OrderByDescending(j => j.ExecutedAtUtc).Take(limit).ToListAsync(cancellationToken);

        var list = await db.MigrationJobs.AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(j => j.ExecutedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(DataMigrationJob job, CancellationToken cancellationToken = default) =>
        await db.MigrationJobs.AddAsync(job, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTenantBackupRepository(DataLifecycleDbContext db) : ITenantBackupRepository
{
    public Task<TenantBackupSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.BackupSnapshots.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.BackupSnapshots.CountAsync(cancellationToken);

    public async Task AddAsync(TenantBackupSnapshot backup, CancellationToken cancellationToken = default) =>
        await db.BackupSnapshots.AddAsync(backup, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfLgpdDataRequestRepository(DataLifecycleDbContext db) : ILgpdDataRequestRepository
{
    public async Task<IReadOnlyList<LgpdDataRequest>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
            return await db.LgpdRequests.AsNoTracking().OrderByDescending(r => r.ProcessedAtUtc).Take(limit).ToListAsync(cancellationToken);

        var list = await db.LgpdRequests.AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(r => r.ProcessedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(LgpdDataRequest request, CancellationToken cancellationToken = default) =>
        await db.LgpdRequests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
