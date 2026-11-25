using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DataLifecycle.Domain;

public enum DataMigrationSourceSystem
{
    Consumer = 1,
    Goomer = 2,
    Saipos = 3,
    GenericCsv = 4
}

public enum DataMigrationStatus
{
    DryRunValidated = 1,
    Completed = 2,
    PartialSuccess = 3,
    Failed = 4
}

public enum TenantBackupStatus
{
    Created = 1,
    Restored = 2,
    IntegrityFailed = 3
}

/// <summary>
/// Trabalho de migração assistida de cardápio/clientes oriundos de sistemas legados (Consumer, Goomer, Saipos) (Seção 31).
/// </summary>
public sealed class DataMigrationJob : TenantScopedAggregateRoot
{
    private DataMigrationJob()
    {
        NormalizedItemsJson = "[]";
        ValidationErrorsJson = "[]";
    }

    private DataMigrationJob(
        Guid id,
        Guid tenantId,
        DataMigrationSourceSystem sourceSystem,
        bool isDryRun,
        int totalRows,
        int importedCount,
        int skippedDuplicateCount,
        int failedCount,
        string normalizedItemsJson,
        string validationErrorsJson,
        Guid requestedByUserId,
        DateTimeOffset executedAtUtc) : base(id, tenantId)
    {
        SourceSystem = sourceSystem;
        IsDryRun = isDryRun;
        TotalRows = totalRows;
        ImportedCount = importedCount;
        SkippedDuplicateCount = skippedDuplicateCount;
        FailedCount = failedCount;
        NormalizedItemsJson = normalizedItemsJson;
        ValidationErrorsJson = validationErrorsJson;
        RequestedByUserId = requestedByUserId;
        ExecutedAtUtc = executedAtUtc;
        Status = isDryRun
            ? (failedCount == totalRows && totalRows > 0 ? DataMigrationStatus.Failed : DataMigrationStatus.DryRunValidated)
            : (importedCount == 0 && failedCount > 0
                ? DataMigrationStatus.Failed
                : failedCount > 0
                    ? DataMigrationStatus.PartialSuccess
                    : DataMigrationStatus.Completed);
    }

    public DataMigrationSourceSystem SourceSystem { get; private set; }
    public bool IsDryRun { get; private set; }
    public DataMigrationStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ImportedCount { get; private set; }
    public int SkippedDuplicateCount { get; private set; }
    public int FailedCount { get; private set; }
    public string NormalizedItemsJson { get; private set; }
    public string ValidationErrorsJson { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset ExecutedAtUtc { get; private set; }

    public static Result<DataMigrationJob> Record(
        Guid tenantId,
        DataMigrationSourceSystem sourceSystem,
        bool isDryRun,
        int totalRows,
        int importedCount,
        int skippedDuplicateCount,
        int failedCount,
        string normalizedItemsJson,
        string validationErrorsJson,
        Guid requestedByUserId,
        DateTimeOffset executedAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DataMigrationJob>(Error.Validation("Migration.TenantRequired", "TenantId é obrigatório."));
        if (requestedByUserId == Guid.Empty)
            return Result.Failure<DataMigrationJob>(Error.Validation("Migration.UserRequired", "Operador responsável é obrigatório."));
        if (totalRows <= 0)
            return Result.Failure<DataMigrationJob>(Error.Validation("Migration.EmptyInput", "Arquivo de origem não contém linhas para migração."));

        return Result.Success(new DataMigrationJob(
            Guid.NewGuid(),
            tenantId,
            sourceSystem,
            isDryRun,
            totalRows,
            Math.Max(0, importedCount),
            Math.Max(0, skippedDuplicateCount),
            Math.Max(0, failedCount),
            string.IsNullOrWhiteSpace(normalizedItemsJson) ? "[]" : normalizedItemsJson,
            string.IsNullOrWhiteSpace(validationErrorsJson) ? "[]" : validationErrorsJson,
            requestedByUserId,
            executedAtUtc));
    }
}

/// <summary>
/// Snapshot de backup cifrado com verificação criptográfica SHA-256 por estabelecimento (Seção 31).
/// </summary>
public sealed class TenantBackupSnapshot : TenantScopedAggregateRoot
{
    private TenantBackupSnapshot()
    {
        BackupCode = string.Empty;
        EncryptedBundleBase64 = string.Empty;
        PlainBundleSha256 = string.Empty;
    }

    private TenantBackupSnapshot(
        Guid id,
        Guid tenantId,
        string backupCode,
        string encryptedBundleBase64,
        string plainBundleSha256,
        long sizeBytes,
        bool includesCatalog,
        bool includesOrders,
        bool includesCustomers,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc) : base(id, tenantId)
    {
        BackupCode = backupCode;
        EncryptedBundleBase64 = encryptedBundleBase64;
        PlainBundleSha256 = plainBundleSha256;
        SizeBytes = sizeBytes;
        IncludesCatalog = includesCatalog;
        IncludesOrders = includesOrders;
        IncludesCustomers = includesCustomers;
        CreatedByUserId = createdByUserId;
        BackupCreatedAtUtc = createdAtUtc;
        Status = TenantBackupStatus.Created;
    }

    public string BackupCode { get; private set; }
    public string EncryptedBundleBase64 { get; private set; }
    public string PlainBundleSha256 { get; private set; }
    public long SizeBytes { get; private set; }
    public bool IncludesCatalog { get; private set; }
    public bool IncludesOrders { get; private set; }
    public bool IncludesCustomers { get; private set; }
    public TenantBackupStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset BackupCreatedAtUtc { get; private set; }
    public Guid? RestoredByUserId { get; private set; }
    public DateTimeOffset? RestoredAtUtc { get; private set; }

    public static Result<TenantBackupSnapshot> Create(
        Guid tenantId,
        string backupCode,
        string encryptedBundleBase64,
        string plainBundleSha256,
        long sizeBytes,
        bool includesCatalog,
        bool includesOrders,
        bool includesCustomers,
        Guid createdByUserId,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantBackupSnapshot>(Error.Validation("Backup.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(backupCode))
            return Result.Failure<TenantBackupSnapshot>(Error.Validation("Backup.CodeRequired", "Código do backup é obrigatório."));
        if (string.IsNullOrWhiteSpace(encryptedBundleBase64) || string.IsNullOrWhiteSpace(plainBundleSha256))
            return Result.Failure<TenantBackupSnapshot>(Error.Validation("Backup.PayloadRequired", "Conteúdo cifrado e hash SHA-256 são obrigatórios."));
        if (createdByUserId == Guid.Empty)
            return Result.Failure<TenantBackupSnapshot>(Error.Validation("Backup.UserRequired", "Usuário criador é obrigatório."));

        return Result.Success(new TenantBackupSnapshot(
            Guid.NewGuid(),
            tenantId,
            backupCode.Trim().ToUpperInvariant(),
            encryptedBundleBase64.Trim(),
            plainBundleSha256.Trim().ToLowerInvariant(),
            Math.Max(0, sizeBytes),
            includesCatalog,
            includesOrders,
            includesCustomers,
            createdByUserId,
            utcNow));
    }

    public Result VerifyAndMarkRestored(string decryptedSha256, Guid restoredByUserId, DateTimeOffset utcNow)
    {
        if (!string.Equals(PlainBundleSha256, decryptedSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Status = TenantBackupStatus.IntegrityFailed;
            return Result.Failure(Error.Conflict("Backup.IntegrityCheckFailed", "Falha na verificação SHA-256 do backup: conteúdo corrompido ou adulterado."));
        }

        Status = TenantBackupStatus.Restored;
        RestoredByUserId = restoredByUserId;
        RestoredAtUtc = utcNow;
        return Result.Success();
    }
}
