using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Marketplaces.Application.Connectors;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.Marketplaces.Infrastructure.Gateways;
using Pitako.Modules.Marketplaces.Infrastructure.Persistence;
using Pitako.Modules.Marketplaces.Infrastructure.Security;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class MarketplaceBulkIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly AesGcmSecretProtector _protector = AesGcmSecretProtector.FromPassphrase("pitako-bulk-sec");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private MarketplacesDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<MarketplacesDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverTargetOrRetry_TenantB_BulkJobsOrStores_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var courierA = User.CreateEstablishmentUser(tenantA, "Entregador A", "ent@a.com", "hash", EstablishmentRole.Courier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid bindBId;
        Guid bulkJobBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var hubB = new MarketplaceHubService(
                new EfMarketplacePartnerAccountRepository(dbB),
                new EfMarketplaceStoreBindingRepository(dbB),
                new EfMarketplaceInboundEventRepository(dbB),
                new EfMarketplaceCatalogMappingRepository(dbB),
                _protector,
                new MarketplaceConnectorRegistry(),
                new UnavailableExternalMarketplaceGateway(),
                ctxB,
                _clock);

            var bulkB = new MarketplaceBulkSyncService(
                new EfMarketplacePartnerAccountRepository(dbB),
                new EfMarketplaceStoreBindingRepository(dbB),
                new EfMarketplaceCatalogMappingRepository(dbB),
                new EfMarketplaceBulkJobRepository(dbB),
                new MarketplaceConnectorRegistry(),
                new UnavailableExternalMarketplaceGateway(),
                ctxB,
                _clock);

            var accB = (await hubB.ConnectPartnerAccountAsync(adminB, MarketplaceProvider.IFood, "iFood B", "cli-b", "sec-b")).Value;
            var bindB = (await hubB.BindExternalStoreAsync(adminB, accB.Id, "B-900", "Loja B")).Value;
            bindBId = bindB.Id;
            var jobB = (await bulkB.BulkToggleStoresOpenCloseAsync(adminB, [bindBId], pauseStore: true)).Value;
            bulkJobBId = jobB.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var bulkA = new MarketplaceBulkSyncService(
            new EfMarketplacePartnerAccountRepository(dbA),
            new EfMarketplaceStoreBindingRepository(dbA),
            new EfMarketplaceCatalogMappingRepository(dbA),
            new EfMarketplaceBulkJobRepository(dbA),
            new MarketplaceConnectorRegistry(),
            new UnavailableExternalMarketplaceGateway(),
            ctxA,
            _clock);

        // Global Query Filters isolam BulkJobs e BulkJobItems de B
        Assert.Empty(await dbA.BulkJobs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.BulkJobItems.AsNoTracking().ToListAsync());

        // Tenant A não consegue incluir StoreBinding B em ação em massa nem reprocessar lote B
        var crossTarget = await bulkA.BulkToggleStoresOpenCloseAsync(managerA, [bindBId], pauseStore: true);
        Assert.True(crossTarget.IsFailure);
        Assert.Equal("Marketplace.StoreBindingNotFound", crossTarget.Error.Code);

        var crossRetry = await bulkA.RetryFailedItemsAsync(managerA, bulkJobBId);
        Assert.True(crossRetry.IsFailure);
        Assert.Equal("BulkJob.NotFound", crossRetry.Error.Code);

        // Entregador sem permissão em Marketplaces
        var courierBlocked = await bulkA.GetMultiStoreHealthDashboardAsync(courierA);
        Assert.True(courierBlocked.IsFailure);
        Assert.Equal("Marketplaces.Forbidden", courierBlocked.Error.Code);
    }
}
