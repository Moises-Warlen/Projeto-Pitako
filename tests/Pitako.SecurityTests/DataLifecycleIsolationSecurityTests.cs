using System.Text.Json;
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
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecuritySpoofBackupGateway(Guid bundleTenantId) : ITenantDataLifecycleGateway
{
    public Task<Result<int>> ImportMigratedCatalogItemsAsync(Guid tenantId, IReadOnlyList<MigratedCatalogItemInput> items, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(items.Count));

    public Task<Result<TenantBackupBundle>> ExtractTenantBackupAsync(Guid tenantId, bool includeCatalog, bool includeOrders, bool includeCustomers, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new TenantBackupBundle(bundleTenantId, DateTimeOffset.UtcNow, includeCatalog, includeOrders, includeCustomers, "{}")));

    public Task<Result> RestoreTenantBackupAsync(Guid tenantId, TenantBackupBundle bundle, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result<string>> ExportSubjectPortabilityJsonAsync(Guid tenantId, string subjectIdentifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success("{}"));

    public Task<Result<int>> AnonymizeSubjectPiiAsync(Guid tenantId, string subjectIdentifier, string anonymousToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(1));
}

public sealed class DataLifecycleIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly BackupAesGcmEncryptionProvider _cipher = BackupAesGcmEncryptionProvider.FromPassphrase("pitako-sec-bkp");

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
    public async Task TenantA_ShouldNeverReadOrRestore_TenantB_BackupsMigrationsOrLgpd_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var financialA = User.CreateEstablishmentUser(tenantA, "Fin A", "fin@a.com", "hash", EstablishmentRole.Financial).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid backupBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new DataLifecycleService(
                new EfDataMigrationJobRepository(dbB),
                new EfTenantBackupRepository(dbB),
                new EfLgpdDataRequestRepository(dbB),
                _cipher,
                new SecuritySpoofBackupGateway(tenantB),
                ctxB,
                _clock);

            await svcB.RunAssistedMigrationAsync(adminB, DataMigrationSourceSystem.Goomer, "sku,category,title,price\nG1,Cat,Item B,10.00");
            var bkpB = (await svcB.CreateEncryptedBackupAsync(adminB)).Value;
            backupBId = bkpB.Id;
            await svcB.AnonymizeCustomerDataAsync(adminB, "b@cliente.com");
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new DataLifecycleService(
            new EfDataMigrationJobRepository(dbA),
            new EfTenantBackupRepository(dbA),
            new EfLgpdDataRequestRepository(dbA),
            _cipher,
            new SecuritySpoofBackupGateway(tenantA),
            ctxA,
            _clock);

        // Global Query Filters isolam migrações, backups e LGPD da Loja B
        Assert.Empty(await dbA.MigrationJobs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.BackupSnapshots.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.LgpdRequests.AsNoTracking().ToListAsync());

        // Cross-tenant restore bloqueado
        var crossRestore = await svcA.RestoreEncryptedBackupAsync(managerA, backupBId);
        Assert.True(crossRestore.IsFailure);
        Assert.Equal("Backup.NotFound", crossRestore.Error.Code);

        // Se o gateway tentar extrair bundle com TenantId divergente, lança CrossTenantAccessException
        var spoofSvc = new DataLifecycleService(
            new EfDataMigrationJobRepository(dbA),
            new EfTenantBackupRepository(dbA),
            new EfLgpdDataRequestRepository(dbA),
            _cipher,
            new SecuritySpoofBackupGateway(tenantB),
            ctxA,
            _clock);
        await Assert.ThrowsAsync<CrossTenantAccessException>(() => spoofSvc.CreateEncryptedBackupAsync(managerA));

        // Backup com payload adulterado no banco falha verificação criptográfica AES-GCM/SHA-256
        var foreignBundleJson = JsonSerializer.Serialize(new TenantBackupBundle(tenantB, _clock.UtcNow, true, true, true, "{}"));
        var tamperedSnap = TenantBackupSnapshot.Create(
            tenantA,
            "BKP-SPOOF",
            _cipher.Protect(foreignBundleJson),
            _cipher.ComputeSha256(foreignBundleJson),
            foreignBundleJson.Length,
            true,
            true,
            true,
            managerA.Id,
            _clock.UtcNow).Value;
        await dbA.BackupSnapshots.AddAsync(tamperedSnap);
        await dbA.SaveChangesAsync();

        await Assert.ThrowsAsync<CrossTenantAccessException>(() => svcA.RestoreEncryptedBackupAsync(managerA, tamperedSnap.Id));

        // RBAC: Financeiro não tem acesso a DataLifecycle/LGPD
        var finBlocked = await svcA.AnonymizeCustomerDataAsync(financialA, "x@y.com");
        Assert.True(finBlocked.IsFailure);
        Assert.Equal("DataLifecycle.Forbidden", finBlocked.Error.Code);
    }
}
