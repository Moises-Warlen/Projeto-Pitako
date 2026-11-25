using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.OfflineSync.Application.Services;

public sealed record OfflineSyncItemOutcome(
    string IdempotencyKey,
    long ClientSequenceNumber,
    OfflineSyncStatus OutcomeStatus,
    Guid? ServerEntityId,
    string? Details);

public sealed record OfflineSyncBatchReport(
    Guid TerminalId,
    int TotalReceived,
    int SyncedCount,
    int DuplicateIgnoredCount,
    int ConflictCount,
    int RejectedCount,
    int TransientFailedCount,
    IReadOnlyList<OfflineSyncItemOutcome> Items)
{
    public bool HasPendingIssues => ConflictCount + RejectedCount + TransientFailedCount > 0;
}

/// <summary>
/// Serviço de sincronização offline-first com ordenação monotônica por sequência,
/// idempotência estrita com verificação de hash SHA-256 contra replay adulterado e resolução auditada de conflitos (Seção 32).
/// </summary>
public sealed class OfflineSynchronizationService
{
    private readonly IOfflineTerminalRepository _terminalRepo;
    private readonly IOfflineSyncOperationRepository _opRepo;
    private readonly IOfflineOperationServerApplier _applier;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public OfflineSynchronizationService(
        IOfflineTerminalRepository terminalRepo,
        IOfflineSyncOperationRepository opRepo,
        IOfflineOperationServerApplier applier,
        ITenantContext tenantContext,
        IClock clock)
    {
        _terminalRepo = terminalRepo;
        _opRepo = opRepo;
        _applier = applier;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<OfflineTerminalRegistration>> RegisterTerminalAsync(
        User approver,
        string terminalCode,
        string rawDeviceFingerprint,
        int catalogSnapshotVersion = 1,
        int maxOfflineHours = 48,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(approver, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<OfflineTerminalRegistration>(guard.Error);

        var normalized = terminalCode.Trim().ToUpperInvariant();
        if (await _terminalRepo.FindByCodeAsync(normalized, cancellationToken) is not null)
            return Result.Failure<OfflineTerminalRegistration>(Error.Conflict("Offline.DuplicateTerminal", $"Terminal offline '{normalized}' já cadastrado."));

        var res = OfflineTerminalRegistration.Register(
            guard.Value,
            normalized,
            rawDeviceFingerprint,
            catalogSnapshotVersion,
            maxOfflineHours,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _terminalRepo.AddAsync(res.Value, cancellationToken);
        await _terminalRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<OfflineSyncBatchReport>> SynchronizeBatchAsync(
        User actor,
        Guid terminalId,
        IReadOnlyList<OfflineOperationInput> operations,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<OfflineSyncBatchReport>(guard.Error);

        if (operations is null || operations.Count == 0)
            return Result.Failure<OfflineSyncBatchReport>(Error.Validation("Offline.EmptyBatch", "Lote de sincronização vazio."));

        var terminal = await _terminalRepo.FindByIdAsync(terminalId, cancellationToken);
        if (terminal is null)
            return Result.Failure<OfflineSyncBatchReport>(Error.NotFound("Offline.TerminalNotFound", "Terminal offline não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, terminal);
        if (!terminal.IsActive)
            return Result.Failure<OfflineSyncBatchReport>(Error.Conflict("Offline.TerminalRevoked", $"Terminal '{terminal.TerminalCode}' foi revogado."));

        var ordered = operations.OrderBy(o => o.ClientSequenceNumber).ToList();
        var outcomes = new List<OfflineSyncItemOutcome>(ordered.Count);

        var synced = 0;
        var duplicates = 0;
        var conflicts = 0;
        var rejected = 0;
        var transient = 0;
        var utcNow = _clock.UtcNow;

        foreach (var input in ordered)
        {
            var normalizedKey = input.IdempotencyKey?.Trim() ?? string.Empty;
            var existing = string.IsNullOrWhiteSpace(normalizedKey)
                ? null
                : await _opRepo.FindByTerminalAndIdempotencyKeyAsync(terminal.Id, normalizedKey, cancellationToken);

            if (existing is not null)
            {
                TenantGuard.EnsureOwnership(_tenantContext, existing);
                var incomingSha = OfflineSyncOperation.ComputePayloadSha256(input.PayloadJson);

                if (!string.Equals(existing.PayloadSha256, incomingSha, StringComparison.Ordinal))
                {
                    conflicts++;
                    outcomes.Add(new OfflineSyncItemOutcome(
                        normalizedKey,
                        input.ClientSequenceNumber,
                        OfflineSyncStatus.ConflictPendingResolution,
                        existing.ServerEntityId,
                        "OfflineSync.IdempotencyPayloadMismatch: payload divergente para chave já sincronizada."));
                    continue;
                }

                duplicates++;
                outcomes.Add(new OfflineSyncItemOutcome(
                    normalizedKey,
                    input.ClientSequenceNumber,
                    OfflineSyncStatus.DuplicateIgnored,
                    existing.ServerEntityId,
                    "Operação já sincronizada anteriormente (idempotente)."));
                continue;
            }

            var createRes = OfflineSyncOperation.Create(
                guard.Value,
                terminal,
                normalizedKey,
                input.ClientSequenceNumber,
                input.OperationType,
                input.EntityReferenceKey,
                input.PayloadJson,
                input.CatalogSnapshotVersionUsed,
                input.ClientOccurredAtUtc,
                utcNow);

            if (createRes.IsFailure)
            {
                rejected++;
                outcomes.Add(new OfflineSyncItemOutcome(
                    normalizedKey,
                    input.ClientSequenceNumber,
                    OfflineSyncStatus.RejectedValidation,
                    null,
                    createRes.Error.Message));
                continue;
            }

            var op = createRes.Value;
            var applyRes = await _applier.ApplyAsync(guard.Value, op, forceOnConflict: false, cancellationToken);

            switch (applyRes.Kind)
            {
                case OfflineApplyOutcomeKind.Applied when applyRes.ServerEntityId.HasValue:
                    op.MarkSynced(applyRes.ServerEntityId.Value, utcNow);
                    synced++;
                    break;
                case OfflineApplyOutcomeKind.Conflict:
                    op.MarkConflict(applyRes.Message ?? "Conflito com estado atual do servidor.", utcNow);
                    conflicts++;
                    break;
                case OfflineApplyOutcomeKind.ValidationRejected:
                    op.MarkValidationRejected(applyRes.Message ?? "Payload inválido.", utcNow);
                    rejected++;
                    break;
                default:
                    op.MarkTransientFailure(applyRes.Message ?? "Falha transitória ao aplicar operação.");
                    transient++;
                    break;
            }

            await _opRepo.AddAsync(op, cancellationToken);
            outcomes.Add(new OfflineSyncItemOutcome(
                op.IdempotencyKey,
                op.ClientSequenceNumber,
                op.SyncStatus,
                op.ServerEntityId,
                op.ConflictDetails));
        }

        terminal.RecordSyncCompleted(utcNow);
        await _opRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new OfflineSyncBatchReport(
            terminal.Id,
            ordered.Count,
            synced,
            duplicates,
            conflicts,
            rejected,
            transient,
            outcomes));
    }

    public async Task<Result<OfflineSyncOperation>> ResolveSyncConflictAsync(
        User approver,
        Guid operationId,
        OfflineConflictResolutionAction action,
        string resolutionNotes,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(approver, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<OfflineSyncOperation>(guard.Error);

        var op = await _opRepo.FindByIdAsync(operationId, cancellationToken);
        if (op is null)
            return Result.Failure<OfflineSyncOperation>(Error.NotFound("Offline.OperationNotFound", "Operação offline não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, op);

        Guid? appliedServerId = null;
        if (action == OfflineConflictResolutionAction.ForceApplyClientWithAudit)
        {
            var forceRes = await _applier.ApplyAsync(guard.Value, op, forceOnConflict: true, cancellationToken);
            if (forceRes.Kind != OfflineApplyOutcomeKind.Applied || !forceRes.ServerEntityId.HasValue)
                return Result.Failure<OfflineSyncOperation>(Error.Conflict("Offline.ForceApplyFailed", forceRes.Message ?? "Não foi possível aplicar operação forçada."));

            appliedServerId = forceRes.ServerEntityId;
        }

        var resolveRes = op.ResolveConflict(action, approver.Id, resolutionNotes, appliedServerId, _clock.UtcNow);
        if (resolveRes.IsFailure)
            return Result.Failure<OfflineSyncOperation>(resolveRes.Error);

        await _opRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(op);
    }

    public async Task<Result<IReadOnlyList<OfflineSyncOperation>>> ListPendingConflictsAsync(
        User actor,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<IReadOnlyList<OfflineSyncOperation>>(guard.Error);

        var list = await _opRepo.ListConflictsAsync(cancellationToken);
        return Result.Success(list);
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de sincronizar terminal offline de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Pdv, action))
            return Result.Failure<Guid>(Error.Forbidden("OfflineSync.Forbidden", "Permissão insuficiente para operar sincronização offline."));

        return Result.Success(tenantId);
    }
}
