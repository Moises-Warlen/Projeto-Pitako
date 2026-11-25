using Pitako.Modules.OfflineSync.Domain;

namespace Pitako.Modules.OfflineSync.Application.Abstractions;

public sealed record OfflineOperationInput(
    string IdempotencyKey,
    long ClientSequenceNumber,
    OfflineOperationType OperationType,
    string EntityReferenceKey,
    string PayloadJson,
    int CatalogSnapshotVersionUsed,
    DateTimeOffset ClientOccurredAtUtc);

public enum OfflineApplyOutcomeKind
{
    Applied = 1,
    Conflict = 2,
    ValidationRejected = 3,
    TransientError = 4
}

public sealed record OfflineApplyResult(
    OfflineApplyOutcomeKind Kind,
    Guid? ServerEntityId,
    string? Message = null);

public interface IOfflineTerminalRepository
{
    Task<OfflineTerminalRegistration?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OfflineTerminalRegistration?> FindByCodeAsync(string terminalCode, CancellationToken cancellationToken = default);
    Task AddAsync(OfflineTerminalRegistration terminal, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOfflineSyncOperationRepository
{
    Task<OfflineSyncOperation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OfflineSyncOperation?> FindByTerminalAndIdempotencyKeyAsync(Guid terminalId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfflineSyncOperation>> ListConflictsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(OfflineSyncOperation operation, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Porta de aplicação no servidor de operações geradas offline pelos terminais (vendas PDV, pedidos e caixa).
/// </summary>
public interface IOfflineOperationServerApplier
{
    Task<OfflineApplyResult> ApplyAsync(
        Guid tenantId,
        OfflineSyncOperation operation,
        bool forceOnConflict,
        CancellationToken cancellationToken = default);
}
