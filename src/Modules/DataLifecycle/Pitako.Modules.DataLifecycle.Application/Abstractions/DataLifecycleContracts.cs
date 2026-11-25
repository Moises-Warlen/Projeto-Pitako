using Pitako.Modules.DataLifecycle.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DataLifecycle.Application.Abstractions;

public sealed record MigratedCatalogItemInput(
    string ExternalSku,
    string CategoryName,
    string ItemTitle,
    decimal UnitPriceAmount,
    string? Description = null);

public sealed record TenantBackupBundle(
    Guid TenantId,
    DateTimeOffset CreatedAtUtc,
    bool IncludesCatalog,
    bool IncludesOrders,
    bool IncludesCustomers,
    string DataPayloadJson);

public interface IDataMigrationJobRepository
{
    Task<DataMigrationJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataMigrationJob>> ListRecentAsync(int limit = 30, CancellationToken cancellationToken = default);
    Task AddAsync(DataMigrationJob job, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITenantBackupRepository
{
    Task<TenantBackupSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TenantBackupSnapshot backup, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ILgpdDataRequestRepository
{
    Task<IReadOnlyList<LgpdDataRequest>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task AddAsync(LgpdDataRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IBackupEncryptionProvider
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
    string ComputeSha256(string plaintext);
}

/// <summary>
/// Porta de integração operacional para persistência de cardápio migrado, extração/restauração de backup
/// e portabilidade/anonimização LGPD restrita ao tenant ativo (Seção 31).
/// </summary>
public interface ITenantDataLifecycleGateway
{
    Task<Result<int>> ImportMigratedCatalogItemsAsync(
        Guid tenantId,
        IReadOnlyList<MigratedCatalogItemInput> items,
        CancellationToken cancellationToken = default);

    Task<Result<TenantBackupBundle>> ExtractTenantBackupAsync(
        Guid tenantId,
        bool includeCatalog,
        bool includeOrders,
        bool includeCustomers,
        CancellationToken cancellationToken = default);

    Task<Result> RestoreTenantBackupAsync(
        Guid tenantId,
        TenantBackupBundle bundle,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportSubjectPortabilityJsonAsync(
        Guid tenantId,
        string subjectIdentifier,
        CancellationToken cancellationToken = default);

    Task<Result<int>> AnonymizeSubjectPiiAsync(
        Guid tenantId,
        string subjectIdentifier,
        string anonymousToken,
        CancellationToken cancellationToken = default);
}
