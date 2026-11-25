using Microsoft.EntityFrameworkCore;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Domain;

namespace Pitako.Modules.OfflineSync.Infrastructure.Persistence;

public sealed class EfOfflineTerminalRepository(OfflineSyncDbContext db) : IOfflineTerminalRepository
{
    public Task<OfflineTerminalRegistration?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Terminals.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<OfflineTerminalRegistration?> FindByCodeAsync(string terminalCode, CancellationToken cancellationToken = default) =>
        db.Terminals.FirstOrDefaultAsync(t => t.TerminalCode == terminalCode, cancellationToken);

    public async Task AddAsync(OfflineTerminalRegistration terminal, CancellationToken cancellationToken = default) =>
        await db.Terminals.AddAsync(terminal, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfOfflineSyncOperationRepository(OfflineSyncDbContext db) : IOfflineSyncOperationRepository
{
    public Task<OfflineSyncOperation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.SyncOperations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<OfflineSyncOperation?> FindByTerminalAndIdempotencyKeyAsync(
        Guid terminalId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        db.SyncOperations.FirstOrDefaultAsync(
            o => o.OfflineTerminalId == terminalId && o.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<OfflineSyncOperation>> ListConflictsAsync(CancellationToken cancellationToken = default)
    {
        var query = db.SyncOperations
            .AsNoTracking()
            .Where(o => o.SyncStatus == OfflineSyncStatus.ConflictPendingResolution);

        if (db.Database.IsNpgsql())
            return await query.OrderBy(o => o.ClientOccurredAtUtc).ToListAsync(cancellationToken);

        var list = await query.ToListAsync(cancellationToken);
        return list.OrderBy(o => o.ClientOccurredAtUtc).ToList();
    }

    public async Task AddAsync(OfflineSyncOperation operation, CancellationToken cancellationToken = default) =>
        await db.SyncOperations.AddAsync(operation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
