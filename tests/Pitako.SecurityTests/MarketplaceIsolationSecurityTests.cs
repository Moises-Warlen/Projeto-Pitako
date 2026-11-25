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

public sealed class MarketplaceIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly AesGcmSecretProtector _protector = AesGcmSecretProtector.FromPassphrase("pitako-sec-key");

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
    public async Task TenantA_ShouldNeverReadSpoofWebhookOrOperate_TenantB_Marketplaces_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var waiterA = User.CreateEstablishmentUser(tenantA, "Garçom A", "gar@a.com", "hash", EstablishmentRole.Waiter).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid bindBId;

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

            var accB = (await hubB.ConnectPartnerAccountAsync(adminB, MarketplaceProvider.IFood, "iFood B", "cli-b", "sec-b")).Value;
            var bindB = (await hubB.BindExternalStoreAsync(adminB, accB.Id, "MERCH-B-999", "Loja B iFood")).Value;
            bindBId = bindB.Id;
            await hubB.MapCatalogProductAsync(adminB, bindBId, Guid.NewGuid(), "Produto B", "SKU-B", 30m);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var hubA = new MarketplaceHubService(
            new EfMarketplacePartnerAccountRepository(dbA),
            new EfMarketplaceStoreBindingRepository(dbA),
            new EfMarketplaceInboundEventRepository(dbA),
            new EfMarketplaceCatalogMappingRepository(dbA),
            _protector,
            new MarketplaceConnectorRegistry(),
            new UnavailableExternalMarketplaceGateway(),
            ctxA,
            _clock);

        // Global Query Filter oculta todas as entidades da Loja B
        Assert.Empty(await dbA.PartnerAccounts.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.StoreBindings.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.InboundEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.CatalogMappings.AsNoTracking().ToListAsync());

        // Spoof cross-tenant de webhook com merchant da Loja B falha
        var spoofWebhook = await hubA.ReceiveAndNormalizeWebhookAsync(
            MarketplaceProvider.IFood,
            "EVT-SPOOF-1",
            "MERCH-B-999",
            "PLACED",
            "{}",
            "sha256=fake");
        Assert.True(spoofWebhook.IsFailure);
        Assert.Equal("Marketplace.StoreBindingNotFound", spoofWebhook.Error.Code);

        // Ação direta em StoreBinding da Loja B falha
        var crossAction = await hubA.ExecuteBindingActionAsync(managerA, bindBId, MarketplaceActionKind.ConfirmOrder, "ORD-1");
        Assert.True(crossAction.IsFailure);
        Assert.Equal("Marketplace.StoreBindingNotFound", crossAction.Error.Code);

        // Assinatura HMAC forjada rejeitada na própria Loja A
        var accA = (await hubA.ConnectPartnerAccountAsync(managerA, MarketplaceProvider.IFood, "iFood A", "cli-a", "segredo-a-real")).Value;
        await hubA.BindExternalStoreAsync(managerA, accA.Id, "MERCH-A-001", "Loja A iFood");

        var badSig = await hubA.ReceiveAndNormalizeWebhookAsync(
            MarketplaceProvider.IFood,
            "EVT-BADSIG",
            "MERCH-A-001",
            "PLACED",
            """{"orderId":"1","items":[{"name":"X","quantity":1,"unitPrice":10}]}""",
            "sha256=forged");
        Assert.True(badSig.IsFailure);
        Assert.Equal("Marketplace.InvalidWebhookSignature", badSig.Error.Code);

        // RBAC: Cozinha e Garçom não possuem acesso ao Hub de Marketplaces
        var kitchenTry = await hubA.ConnectPartnerAccountAsync(kitchenA, MarketplaceProvider.Rappi, "Rappi", "c", "s");
        Assert.True(kitchenTry.IsFailure);
        Assert.Equal("Marketplaces.Forbidden", kitchenTry.Error.Code);

        var waiterTry = await hubA.ConnectPartnerAccountAsync(waiterA, MarketplaceProvider.Rappi, "Rappi", "c", "s");
        Assert.True(waiterTry.IsFailure);
        Assert.Equal("Marketplaces.Forbidden", waiterTry.Error.Code);
    }
}
