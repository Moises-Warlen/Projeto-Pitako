using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Application.Services;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.Modules.DataLifecycle.Infrastructure.Persistence;
using Pitako.Modules.DataLifecycle.Infrastructure.Security;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class InMemoryTenantDataLifecycleGateway(Guid tenantId) : ITenantDataLifecycleGateway
{
    public List<MigratedCatalogItemInput> ImportedItems { get; } = [];
    public Dictionary<string, string> CustomersPii { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maria@cliente.com"] = "Maria Silva | Tel 11988887777"
    };
    public int RestoreCount { get; private set; }

    public Task<Result<int>> ImportMigratedCatalogItemsAsync(Guid tId, IReadOnlyList<MigratedCatalogItemInput> items, CancellationToken cancellationToken = default)
    {
        ImportedItems.AddRange(items);
        return Task.FromResult(Result.Success(items.Count));
    }

    public Task<Result<TenantBackupBundle>> ExtractTenantBackupAsync(Guid tId, bool includeCatalog, bool includeOrders, bool includeCustomers, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new TenantBackupBundle(
            tenantId,
            DateTimeOffset.UtcNow,
            includeCatalog,
            includeOrders,
            includeCustomers,
            $"{{\"items\":{ImportedItems.Count},\"customers\":{CustomersPii.Count}}}")));

    public Task<Result> RestoreTenantBackupAsync(Guid tId, TenantBackupBundle bundle, CancellationToken cancellationToken = default)
    {
        RestoreCount++;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<string>> ExportSubjectPortabilityJsonAsync(Guid tId, string subjectIdentifier, CancellationToken cancellationToken = default)
    {
        CustomersPii.TryGetValue(subjectIdentifier, out var data);
        return Task.FromResult(Result.Success($"{{\"subject\":\"{subjectIdentifier}\",\"data\":\"{data ?? "none"}\"}}"));
    }

    public Task<Result<int>> AnonymizeSubjectPiiAsync(Guid tId, string subjectIdentifier, string anonymousToken, CancellationToken cancellationToken = default)
    {
        if (!CustomersPii.ContainsKey(subjectIdentifier))
            return Task.FromResult(Result.Success(0));

        CustomersPii[subjectIdentifier] = anonymousToken;
        return Task.FromResult(Result.Success(1));
    }
}

public sealed class DataLifecycleWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 59, 0, TimeSpan.Zero));
    private readonly BackupAesGcmEncryptionProvider _cipher = BackupAesGcmEncryptionProvider.FromPassphrase("pitako-bkp-key");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private DataLifecycleDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DataLifecycleDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task DataLifecycle_Migration_EncryptedBackupAndRestore_AndLgpdFlows_ShouldPersistCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var gateway = new InMemoryTenantDataLifecycleGateway(tenantId);

        await using var db = CreateDb(ctx);
        var svc = new DataLifecycleService(
            new EfDataMigrationJobRepository(db),
            new EfTenantBackupRepository(db),
            new EfLgpdDataRequestRepository(db),
            _cipher,
            gateway,
            ctx,
            _clock);

        // 1. Migração Consumer: dry-run depois importação efetiva
        const string csv = """
            codigo_pdv;categoria;nome_produto;preco
            SKU-1;Açaís;Açaí 500ml;22,00
            SKU-2;Lanches;Coxinha;invalid
            """;

        var dryRun = (await svc.RunAssistedMigrationAsync(manager, DataMigrationSourceSystem.Consumer, csv, dryRunOnly: true)).Value;
        Assert.Equal(DataMigrationStatus.DryRunValidated, dryRun.Status);
        Assert.Empty(gateway.ImportedItems);

        var imported = (await svc.RunAssistedMigrationAsync(manager, DataMigrationSourceSystem.Consumer, csv, dryRunOnly: false)).Value;
        Assert.Equal(DataMigrationStatus.PartialSuccess, imported.Status);
        Assert.Single(gateway.ImportedItems);

        // 2. Backup cifrado AES-GCM + restauração verificada SHA-256
        var backup = (await svc.CreateEncryptedBackupAsync(manager)).Value;
        Assert.StartsWith("v1:", backup.EncryptedBundleBase64, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportedItems", backup.EncryptedBundleBase64);

        var restored = (await svc.RestoreEncryptedBackupAsync(manager, backup.Id)).Value;
        Assert.Equal(TenantBackupStatus.Restored, restored.Status);
        Assert.Equal(1, gateway.RestoreCount);

        // 3. LGPD: portabilidade + anonimização preservando integridade
        var port = (await svc.ExportLgpdPortabilityAsync(manager, "maria@cliente.com")).Value;
        Assert.Contains("Maria Silva", port.ExportedPortabilityJson);
        Assert.Equal("ma***om", port.SubjectMasked);

        var anon = (await svc.AnonymizeCustomerDataAsync(manager, "maria@cliente.com")).Value;
        Assert.Equal(1, anon.AnonymizedRecordsCount);
        Assert.StartsWith("ANON-", gateway.CustomersPii["maria@cliente.com"], StringComparison.Ordinal);
    }
}
