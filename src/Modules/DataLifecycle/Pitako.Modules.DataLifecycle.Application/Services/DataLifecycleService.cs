using System.Text.Json;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Application.Parsers;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.DataLifecycle.Application.Services;

/// <summary>
/// Serviço de Ciclo de Vida de Dados: Migração assistida (Consumer, Goomer, Saipos),
/// Backup cifrado com verificação SHA-256 e Fluxos de Conformidade LGPD (Seção 31).
/// </summary>
public sealed class DataLifecycleService
{
    private readonly IDataMigrationJobRepository _migrationRepo;
    private readonly ITenantBackupRepository _backupRepo;
    private readonly ILgpdDataRequestRepository _lgpdRepo;
    private readonly IBackupEncryptionProvider _encryptionProvider;
    private readonly ITenantDataLifecycleGateway _gateway;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public DataLifecycleService(
        IDataMigrationJobRepository migrationRepo,
        ITenantBackupRepository backupRepo,
        ILgpdDataRequestRepository lgpdRepo,
        IBackupEncryptionProvider encryptionProvider,
        ITenantDataLifecycleGateway gateway,
        ITenantContext tenantContext,
        IClock clock)
    {
        _migrationRepo = migrationRepo;
        _backupRepo = backupRepo;
        _lgpdRepo = lgpdRepo;
        _encryptionProvider = encryptionProvider;
        _gateway = gateway;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<DataMigrationJob>> RunAssistedMigrationAsync(
        User actor,
        DataMigrationSourceSystem sourceSystem,
        string rawCsvContent,
        bool dryRunOnly = false,
        CancellationToken cancellationToken = default)
    {
        var reqAction = dryRunOnly ? PermissionAction.View : PermissionAction.Create;
        var guard = ValidateActor(actor, reqAction);
        if (guard.IsFailure)
            return Result.Failure<DataMigrationJob>(guard.Error);

        var parseRes = LegacyPosMigrationParser.ParseCatalogCsv(sourceSystem, rawCsvContent);
        if (parseRes.IsFailure)
            return Result.Failure<DataMigrationJob>(parseRes.Error);

        var parsed = parseRes.Value;
        var importedCount = parsed.ValidCount;

        if (!dryRunOnly && parsed.ValidItems.Count > 0)
        {
            var importRes = await _gateway.ImportMigratedCatalogItemsAsync(guard.Value, parsed.ValidItems, cancellationToken);
            if (importRes.IsFailure)
                return Result.Failure<DataMigrationJob>(importRes.Error);

            importedCount = importRes.Value;
        }

        var jobRes = DataMigrationJob.Record(
            guard.Value,
            sourceSystem,
            dryRunOnly,
            parsed.TotalRows,
            importedCount,
            parsed.SkippedDuplicateCount,
            parsed.FailedCount,
            JsonSerializer.Serialize(parsed.ValidItems),
            JsonSerializer.Serialize(parsed.Errors),
            actor.Id,
            _clock.UtcNow);

        if (jobRes.IsFailure)
            return jobRes;

        await _migrationRepo.AddAsync(jobRes.Value, cancellationToken);
        await _migrationRepo.SaveChangesAsync(cancellationToken);
        return jobRes;
    }

    public async Task<Result<TenantBackupSnapshot>> CreateEncryptedBackupAsync(
        User actor,
        bool includeCatalog = true,
        bool includeOrders = true,
        bool includeCustomers = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Export);
        if (guard.IsFailure)
            return Result.Failure<TenantBackupSnapshot>(guard.Error);

        var extractRes = await _gateway.ExtractTenantBackupAsync(guard.Value, includeCatalog, includeOrders, includeCustomers, cancellationToken);
        if (extractRes.IsFailure)
            return Result.Failure<TenantBackupSnapshot>(extractRes.Error);

        var bundle = extractRes.Value;
        if (bundle.TenantId != guard.Value)
            throw new CrossTenantAccessException(bundle.TenantId, guard.Value, "Extração de backup violou fronteira multi-tenant.");

        var plainJson = JsonSerializer.Serialize(bundle);
        var sha256 = _encryptionProvider.ComputeSha256(plainJson);
        var encrypted = _encryptionProvider.Protect(plainJson);
        var seq = await _backupRepo.CountAllAsync(cancellationToken) + 1;
        var backupCode = $"BKP-{seq:D4}";

        var snapRes = TenantBackupSnapshot.Create(
            guard.Value,
            backupCode,
            encrypted,
            sha256,
            System.Text.Encoding.UTF8.GetByteCount(plainJson),
            includeCatalog,
            includeOrders,
            includeCustomers,
            actor.Id,
            _clock.UtcNow);

        if (snapRes.IsFailure)
            return snapRes;

        await _backupRepo.AddAsync(snapRes.Value, cancellationToken);
        await _backupRepo.SaveChangesAsync(cancellationToken);
        return snapRes;
    }

    public async Task<Result<TenantBackupSnapshot>> RestoreEncryptedBackupAsync(
        User actor,
        Guid backupId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<TenantBackupSnapshot>(guard.Error);

        var snap = await _backupRepo.FindByIdAsync(backupId, cancellationToken);
        if (snap is null)
            return Result.Failure<TenantBackupSnapshot>(Error.NotFound("Backup.NotFound", "Snapshot de backup não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, snap);

        string decryptedJson;
        try
        {
            decryptedJson = _encryptionProvider.Unprotect(snap.EncryptedBundleBase64);
        }
        catch (Exception)
        {
            snap.VerifyAndMarkRestored("INVALID", actor.Id, _clock.UtcNow);
            await _backupRepo.SaveChangesAsync(cancellationToken);
            return Result.Failure<TenantBackupSnapshot>(Error.Conflict("Backup.IntegrityCheckFailed", "Falha ao decifrar pacote de backup: conteúdo adulterado."));
        }

        var actualSha = _encryptionProvider.ComputeSha256(decryptedJson);
        var verifyRes = snap.VerifyAndMarkRestored(actualSha, actor.Id, _clock.UtcNow);
        if (verifyRes.IsFailure)
        {
            await _backupRepo.SaveChangesAsync(cancellationToken);
            return Result.Failure<TenantBackupSnapshot>(verifyRes.Error);
        }

        var bundle = JsonSerializer.Deserialize<TenantBackupBundle>(decryptedJson);
        if (bundle is null || bundle.TenantId != guard.Value)
            throw new CrossTenantAccessException(bundle?.TenantId ?? Guid.Empty, guard.Value, "Restauração de backup de outro tenant bloqueada.");

        var restoreRes = await _gateway.RestoreTenantBackupAsync(guard.Value, bundle, cancellationToken);
        if (restoreRes.IsFailure)
            return Result.Failure<TenantBackupSnapshot>(restoreRes.Error);

        await _backupRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(snap);
    }

    public async Task<Result<LgpdDataRequest>> ExportLgpdPortabilityAsync(
        User actor,
        string subjectIdentifier,
        string reason = "Solicitação do titular (Art. 18, V LGPD)",
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Export);
        if (guard.IsFailure)
            return Result.Failure<LgpdDataRequest>(guard.Error);
        if (string.IsNullOrWhiteSpace(subjectIdentifier))
            return Result.Failure<LgpdDataRequest>(Error.Validation("Lgpd.SubjectRequired", "Identificador do titular é obrigatório."));

        var exportRes = await _gateway.ExportSubjectPortabilityJsonAsync(guard.Value, subjectIdentifier.Trim(), cancellationToken);
        if (exportRes.IsFailure)
            return Result.Failure<LgpdDataRequest>(exportRes.Error);

        var rec = LgpdDataRequest.RecordCompleted(
            guard.Value,
            LgpdRequestType.DataPortabilityExport,
            subjectIdentifier,
            reason,
            exportRes.Value,
            0,
            actor.Id,
            _clock.UtcNow);

        if (rec.IsFailure)
            return rec;

        await _lgpdRepo.AddAsync(rec.Value, cancellationToken);
        await _lgpdRepo.SaveChangesAsync(cancellationToken);
        return rec;
    }

    public async Task<Result<LgpdDataRequest>> AnonymizeCustomerDataAsync(
        User actor,
        string subjectIdentifier,
        string reason = "Solicitação de eliminação/anonimização (Art. 18, VI LGPD)",
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<LgpdDataRequest>(guard.Error);
        if (string.IsNullOrWhiteSpace(subjectIdentifier))
            return Result.Failure<LgpdDataRequest>(Error.Validation("Lgpd.SubjectRequired", "Identificador do titular é obrigatório."));

        var anonToken = LgpdAnonymizer.GenerateAnonymousToken(guard.Value, subjectIdentifier);
        var anonRes = await _gateway.AnonymizeSubjectPiiAsync(guard.Value, subjectIdentifier.Trim(), anonToken, cancellationToken);
        if (anonRes.IsFailure)
            return Result.Failure<LgpdDataRequest>(anonRes.Error);

        var rec = LgpdDataRequest.RecordCompleted(
            guard.Value,
            LgpdRequestType.DataAnonymizationErasure,
            subjectIdentifier,
            reason,
            null,
            anonRes.Value,
            actor.Id,
            _clock.UtcNow);

        if (rec.IsFailure)
            return rec;

        await _lgpdRepo.AddAsync(rec.Value, cancellationToken);
        await _lgpdRepo.SaveChangesAsync(cancellationToken);
        return rec;
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de operar ciclo de dados/LGPD de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.DataLifecycle, action))
            return Result.Failure<Guid>(Error.Forbidden("DataLifecycle.Forbidden", "Permissão insuficiente para migração, backup ou operações LGPD."));

        return Result.Success(tenantId);
    }
}
