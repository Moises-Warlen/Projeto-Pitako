using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Connectors;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.Marketplaces.Infrastructure.Gateways;
using Pitako.Modules.Marketplaces.Infrastructure.Persistence;
using Pitako.Modules.Marketplaces.Infrastructure.Security;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class SelectiveFailingMarketplaceGateway : IMarketplaceRemoteGateway
{
    public HashSet<Guid> FailingBindingIds { get; } = [];
    public List<(Guid BindingId, MarketplaceActionKind Action, string? ExternalId)> ExecutedCalls { get; } = [];

    public Task<Result<string>> ExecuteRemoteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (FailingBindingIds.Contains(binding.Id))
            return Task.FromResult(Result.Failure<string>(Error.Conflict("Marketplace.RemoteTimeout", $"Timeout em {binding.ExternalStoreName}")));

        ExecutedCalls.Add((binding.Id, action, targetExternalId));
        return Task.FromResult(Result.Success("{\"synced\":true}"));
    }
}

public sealed class MarketplaceBulkSyncIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 45, 0, TimeSpan.Zero));
    private readonly AesGcmSecretProtector _protector = AesGcmSecretProtector.FromPassphrase("pitako-bulk-key");

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
    public async Task MultiStoreBulkActions_ShouldReportPartialFailures_RetryOnlyFailed_AndPersistMappings()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@multi.com", "hash", EstablishmentRole.Manager).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var gateway = new SelectiveFailingMarketplaceGateway();
        var productId = Guid.NewGuid();

        Guid ifood1Id;
        Guid ifood2Id;
        Guid rappiId;
        Guid aiqfomeId;
        Guid partialJobId;

        await using (var db = CreateDb(ctx))
        {
            var hub = new MarketplaceHubService(
                new EfMarketplacePartnerAccountRepository(db),
                new EfMarketplaceStoreBindingRepository(db),
                new EfMarketplaceInboundEventRepository(db),
                new EfMarketplaceCatalogMappingRepository(db),
                _protector,
                new MarketplaceConnectorRegistry(),
                gateway,
                ctx,
                _clock);

            var accIfood = (await hub.ConnectPartnerAccountAsync(manager, MarketplaceProvider.IFood, "Rede iFood", "cli-if", "sec-if")).Value;
            var accRappi = (await hub.ConnectPartnerAccountAsync(manager, MarketplaceProvider.Rappi, "Rede Rappi", "cli-rp", "sec-rp")).Value;
            var accAiq = (await hub.ConnectPartnerAccountAsync(manager, MarketplaceProvider.Aiqfome, "Rede Aiqfome", "cli-aq", "sec-aq")).Value;

            var bIfood1 = (await hub.BindExternalStoreAsync(manager, accIfood.Id, "IF-FILIAL-1", "Açaí Filial 1")).Value;
            var bIfood2 = (await hub.BindExternalStoreAsync(manager, accIfood.Id, "IF-FILIAL-2", "Açaí Filial 2")).Value;
            var bRappi = (await hub.BindExternalStoreAsync(manager, accRappi.Id, "RP-FILIAL-1", "Açaí Rappi 1")).Value;
            var bAiq = (await hub.BindExternalStoreAsync(manager, accAiq.Id, "AQ-FILIAL-1", "Açaí Aiqfome 1")).Value;

            ifood1Id = bIfood1.Id;
            ifood2Id = bIfood2.Id;
            rappiId = bRappi.Id;
            aiqfomeId = bAiq.Id;

            await hub.MapCatalogProductAsync(manager, ifood1Id, productId, "Açaí 500ml", "SKU-IF-1", 22.00m);
            await hub.MapCatalogProductAsync(manager, ifood2Id, productId, "Açaí 500ml", "SKU-IF-2", 22.00m);
            await hub.MapCatalogProductAsync(manager, rappiId, productId, "Açaí 500ml", "SKU-RP-1", 22.00m);
            await hub.MapCatalogProductAsync(manager, aiqfomeId, productId, "Açaí 500ml", "SKU-AQ-1", 22.00m);

            var bulkSvc = new MarketplaceBulkSyncService(
                new EfMarketplacePartnerAccountRepository(db),
                new EfMarketplaceStoreBindingRepository(db),
                new EfMarketplaceCatalogMappingRepository(db),
                new EfMarketplaceBulkJobRepository(db),
                new MarketplaceConnectorRegistry(),
                gateway,
                ctx,
                _clock);

            // Simula timeout transitório na Filial 2 iFood + Aiqfome sem suporte a SyncItemPrice
            gateway.FailingBindingIds.Add(ifood2Id);

            var priceJob = (await bulkSvc.BulkSyncProductPriceAsync(
                manager,
                productId,
                25.90m,
                [ifood1Id, ifood2Id, rappiId, aiqfomeId])).Value;

            partialJobId = priceJob.Id;
            Assert.Equal(MarketplaceBulkJobStatus.PartialSuccess, priceJob.Status);
            Assert.False(priceJob.IsGlobalSuccess);
            Assert.Equal(2, priceJob.SucceededCount);
            Assert.Equal(2, priceJob.FailedCount);
            Assert.Equal(MarketplaceBulkItemOutcome.UnsupportedCapability, priceJob.Items.First(i => i.StoreBindingId == aiqfomeId).Outcome);

            // Recupera Filial 2 iFood e reprocessa apenas falhas (Filial 1 e Rappi não são reexecutados!)
            gateway.FailingBindingIds.Remove(ifood2Id);
            gateway.ExecutedCalls.Clear();

            var retried = (await bulkSvc.RetryFailedItemsAsync(manager, partialJobId)).Value;
            Assert.Single(gateway.ExecutedCalls);
            Assert.Equal(ifood2Id, gateway.ExecutedCalls[0].BindingId);
            Assert.Equal(3, retried.SucceededCount);
            Assert.Equal(1, retried.FailedCount); // Aiqfome continua UnsupportedCapability -> nunca mente sucesso global!
            Assert.False(retried.IsGlobalSuccess);

            // Pausa do SKU em todas as 4 lojas (aqui TODAS suportam ToggleItemAvailability -> Completed!)
            var pauseJob = (await bulkSvc.BulkToggleProductAvailabilityAsync(
                manager,
                productId,
                isPaused: true,
                [ifood1Id, ifood2Id, rappiId, aiqfomeId])).Value;

            Assert.Equal(MarketplaceBulkJobStatus.Completed, pauseJob.Status);
            Assert.True(pauseJob.IsGlobalSuccess);
            Assert.Equal(4, pauseJob.SucceededCount);

            var health = (await bulkSvc.GetMultiStoreHealthDashboardAsync(manager)).Value;
            Assert.Equal(4, health.TotalStores);
            Assert.Equal(4, health.ConnectedCount);
        }

        // Sem credenciais externas reais (UnavailableExternalMarketplaceGateway), lote registra falha explícita
        await using (var dbOffline = CreateDb(ctx))
        {
            var bulkOffline = new MarketplaceBulkSyncService(
                new EfMarketplacePartnerAccountRepository(dbOffline),
                new EfMarketplaceStoreBindingRepository(dbOffline),
                new EfMarketplaceCatalogMappingRepository(dbOffline),
                new EfMarketplaceBulkJobRepository(dbOffline),
                new MarketplaceConnectorRegistry(),
                new UnavailableExternalMarketplaceGateway(),
                ctx,
                _clock);

            var failJob = (await bulkOffline.BulkToggleStoresOpenCloseAsync(manager, [ifood1Id, rappiId], pauseStore: true, "Falta energia")).Value;
            Assert.Equal(MarketplaceBulkJobStatus.Failed, failJob.Status);
            Assert.False(failJob.IsGlobalSuccess);
            Assert.Equal(2, failJob.FailedCount);
        }
    }
}
