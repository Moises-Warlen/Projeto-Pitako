using System.Security.Cryptography;
using System.Text;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.OfflineSync.Domain;

public enum OfflineOperationType
{
    PdvSale = 1,
    CounterOrder = 2,
    TableTabItem = 3,
    CashMovement = 4
}

public enum OfflineSyncStatus
{
    Synced = 1,
    DuplicateIgnored = 2,
    ConflictPendingResolution = 3,
    RejectedValidation = 4,
    FailedTransient = 5,
    ResolvedDiscardClient = 6,
    ResolvedForcedApply = 7
}

public enum OfflineConflictResolutionAction
{
    AcceptServerAndDiscardClient = 1,
    ForceApplyClientWithAudit = 2
}

/// <summary>
/// Terminal local autorizado a operar em modo offline com snapshot versionado de catálogo (Seção 32).
/// </summary>
public sealed class OfflineTerminalRegistration : TenantScopedAggregateRoot
{
    private OfflineTerminalRegistration()
    {
        TerminalCode = string.Empty;
        DeviceFingerprintHash = string.Empty;
    }

    private OfflineTerminalRegistration(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string terminalCode,
        string deviceFingerprintHash,
        int catalogSnapshotVersion,
        int maxOfflineHours) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        TerminalCode = terminalCode;
        DeviceFingerprintHash = deviceFingerprintHash;
        CatalogSnapshotVersion = catalogSnapshotVersion;
        MaxOfflineHours = maxOfflineHours;
        IsActive = true;
    }

    public Guid? StoreUnitId { get; private set; }
    public string TerminalCode { get; private set; }
    public string DeviceFingerprintHash { get; private set; }
    public int CatalogSnapshotVersion { get; private set; }
    public int MaxOfflineHours { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastSyncUtc { get; private set; }

    public static Result<OfflineTerminalRegistration> Register(
        Guid tenantId,
        string terminalCode,
        string rawDeviceFingerprint,
        int catalogSnapshotVersion = 1,
        int maxOfflineHours = 48,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<OfflineTerminalRegistration>(Error.Validation("Offline.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(terminalCode))
            return Result.Failure<OfflineTerminalRegistration>(Error.Validation("Offline.TerminalCodeRequired", "Código do terminal é obrigatório."));
        if (string.IsNullOrWhiteSpace(rawDeviceFingerprint))
            return Result.Failure<OfflineTerminalRegistration>(Error.Validation("Offline.FingerprintRequired", "Identificação do dispositivo é obrigatória."));
        if (maxOfflineHours is < 1 or > 720)
            return Result.Failure<OfflineTerminalRegistration>(Error.Validation("Offline.InvalidMaxHours", "Janela offline máxima deve estar entre 1h e 720h."));

        var fpHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{rawDeviceFingerprint.Trim()}"))).ToLowerInvariant();
        return Result.Success(new OfflineTerminalRegistration(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            terminalCode.Trim().ToUpperInvariant(),
            fpHash,
            Math.Max(1, catalogSnapshotVersion),
            maxOfflineHours));
    }

    public void BumpCatalogSnapshotVersion(int newVersion, DateTimeOffset syncedAtUtc)
    {
        if (newVersion > CatalogSnapshotVersion)
            CatalogSnapshotVersion = newVersion;
        LastSyncUtc = syncedAtUtc;
    }

    public void RecordSyncCompleted(DateTimeOffset syncedAtUtc) => LastSyncUtc = syncedAtUtc;
    public void Revoke() => IsActive = false;
}

/// <summary>
/// Operação gerada em modo offline com chave de idempotência, ordem monotônica,
/// verificação SHA-256 e tratamento auditado de conflitos na reconexão (Seção 32).
/// </summary>
public sealed class OfflineSyncOperation : TenantScopedAggregateRoot
{
    private OfflineSyncOperation()
    {
        IdempotencyKey = string.Empty;
        EntityReferenceKey = string.Empty;
        PayloadJson = "{}";
        PayloadSha256 = string.Empty;
    }

    private OfflineSyncOperation(
        Guid id,
        Guid tenantId,
        Guid offlineTerminalId,
        string idempotencyKey,
        long clientSequenceNumber,
        OfflineOperationType operationType,
        string entityReferenceKey,
        string payloadJson,
        string payloadSha256,
        int catalogSnapshotVersionUsed,
        DateTimeOffset clientOccurredAtUtc) : base(id, tenantId)
    {
        OfflineTerminalId = offlineTerminalId;
        IdempotencyKey = idempotencyKey;
        ClientSequenceNumber = clientSequenceNumber;
        OperationType = operationType;
        EntityReferenceKey = entityReferenceKey;
        PayloadJson = payloadJson;
        PayloadSha256 = payloadSha256;
        CatalogSnapshotVersionUsed = catalogSnapshotVersionUsed;
        ClientOccurredAtUtc = clientOccurredAtUtc;
        SyncStatus = OfflineSyncStatus.FailedTransient;
        RetryCount = 0;
    }

    public Guid OfflineTerminalId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long ClientSequenceNumber { get; private set; }
    public OfflineOperationType OperationType { get; private set; }
    public string EntityReferenceKey { get; private set; }
    public string PayloadJson { get; private set; }
    public string PayloadSha256 { get; private set; }
    public int CatalogSnapshotVersionUsed { get; private set; }
    public DateTimeOffset ClientOccurredAtUtc { get; private set; }
    public OfflineSyncStatus SyncStatus { get; private set; }
    public Guid? ServerEntityId { get; private set; }
    public string? ConflictDetails { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset? SyncedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public int RetryCount { get; private set; }

    public static string ComputePayloadSha256(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson ?? "{}"))).ToLowerInvariant();

    public static Result<OfflineSyncOperation> Create(
        Guid tenantId,
        OfflineTerminalRegistration terminal,
        string idempotencyKey,
        long clientSequenceNumber,
        OfflineOperationType operationType,
        string entityReferenceKey,
        string payloadJson,
        int catalogSnapshotVersionUsed,
        DateTimeOffset clientOccurredAtUtc,
        DateTimeOffset serverUtcNow)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (tenantId == Guid.Empty)
            return Result.Failure<OfflineSyncOperation>(Error.Validation("Offline.TenantRequired", "TenantId é obrigatório."));
        if (terminal.TenantId != tenantId)
            return Result.Failure<OfflineSyncOperation>(Error.Forbidden("Offline.CrossTenantTerminal", "Terminal pertence a outro estabelecimento."));
        if (!terminal.IsActive)
            return Result.Failure<OfflineSyncOperation>(Error.Conflict("Offline.TerminalRevoked", $"Terminal '{terminal.TerminalCode}' foi revogado."));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<OfflineSyncOperation>(Error.Validation("Offline.IdempotencyRequired", "Chave de idempotência é obrigatória."));
        if (clientSequenceNumber <= 0)
            return Result.Failure<OfflineSyncOperation>(Error.Validation("Offline.InvalidSequence", "Sequência do cliente deve ser positiva."));
        if (clientOccurredAtUtc > serverUtcNow.AddMinutes(5))
            return Result.Failure<OfflineSyncOperation>(Error.Validation("Offline.FutureTimestamp", "Timestamp offline excede tolerância de relógio futuro."));
        if ((serverUtcNow - clientOccurredAtUtc).TotalHours > terminal.MaxOfflineHours)
            return Result.Failure<OfflineSyncOperation>(Error.Conflict("Offline.MaxOfflineWindowExceeded", $"Operação excedeu a janela offline máxima de {terminal.MaxOfflineHours}h."));

        var normalizedJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson.Trim();
        return Result.Success(new OfflineSyncOperation(
            Guid.NewGuid(),
            tenantId,
            terminal.Id,
            idempotencyKey.Trim(),
            clientSequenceNumber,
            operationType,
            string.IsNullOrWhiteSpace(entityReferenceKey) ? operationType.ToString() : entityReferenceKey.Trim(),
            normalizedJson,
            ComputePayloadSha256(normalizedJson),
            catalogSnapshotVersionUsed,
            clientOccurredAtUtc));
    }

    public void MarkSynced(Guid serverEntityId, DateTimeOffset syncedAtUtc)
    {
        RetryCount++;
        ServerEntityId = serverEntityId;
        SyncStatus = OfflineSyncStatus.Synced;
        ConflictDetails = null;
        SyncedAtUtc = syncedAtUtc;
    }

    public void MarkConflict(string conflictDetails, DateTimeOffset utcNow)
    {
        RetryCount++;
        SyncStatus = OfflineSyncStatus.ConflictPendingResolution;
        ConflictDetails = string.IsNullOrWhiteSpace(conflictDetails) ? "Conflito detectado na sincronização." : conflictDetails.Trim();
        SyncedAtUtc = utcNow;
    }

    public void MarkValidationRejected(string reason, DateTimeOffset utcNow)
    {
        RetryCount++;
        SyncStatus = OfflineSyncStatus.RejectedValidation;
        ConflictDetails = reason.Trim();
        SyncedAtUtc = utcNow;
    }

    public void MarkTransientFailure(string reason)
    {
        RetryCount++;
        SyncStatus = OfflineSyncStatus.FailedTransient;
        ConflictDetails = reason.Trim();
    }

    public Result ResolveConflict(
        OfflineConflictResolutionAction action,
        Guid resolverUserId,
        string resolutionNotes,
        Guid? appliedServerEntityId,
        DateTimeOffset resolvedAtUtc)
    {
        if (SyncStatus != OfflineSyncStatus.ConflictPendingResolution)
            return Result.Failure(Error.Conflict("Offline.NotInConflict", "Apenas operações em conflito pendente podem ser resolvidas."));
        if (resolverUserId == Guid.Empty)
            return Result.Failure(Error.Validation("Offline.ResolverRequired", "Responsável pela resolução é obrigatório."));
        if (string.IsNullOrWhiteSpace(resolutionNotes))
            return Result.Failure(Error.Validation("Offline.NotesRequired", "Justificativa da resolução de conflito é obrigatória."));

        ResolvedByUserId = resolverUserId;
        ResolutionNotes = resolutionNotes.Trim();
        ResolvedAtUtc = resolvedAtUtc;

        if (action == OfflineConflictResolutionAction.AcceptServerAndDiscardClient)
        {
            SyncStatus = OfflineSyncStatus.ResolvedDiscardClient;
        }
        else
        {
            ServerEntityId = appliedServerEntityId;
            SyncStatus = OfflineSyncStatus.ResolvedForcedApply;
        }

        return Result.Success();
    }
}
