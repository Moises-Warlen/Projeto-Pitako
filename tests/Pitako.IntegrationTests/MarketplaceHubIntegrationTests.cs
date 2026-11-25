using System.Security.Cryptography;
using System.Text;
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
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class MarketplaceHubIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 30, 0, TimeSpan.Zero));
    private readonly AesGcmSecretProtector _protector = AesGcmSecretProtector.FromPassphrase("pitako-test-master-key-2026");

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
    public async Task MarketplaceHub_ShouldEncryptSecrets_NormalizeIdempotentWebhooks_AndEnforceRealCapabilitiesAndOfflineGateway()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid ifoodBindingId;
        Guid aiqfomeBindingId;
        const string ifoodSecret = "ifood-client-secret-super-secreto";

        await using (var db = CreateDb(ctx))
        {
            var hub = new MarketplaceHubService(
                new EfMarketplacePartnerAccountRepository(db),
                new EfMarketplaceStoreBindingRepository(db),
                new EfMarketplaceInboundEventRepository(db),
                new EfMarketplaceCatalogMappingRepository(db),
                _protector,
                new MarketplaceConnectorRegistry(),
                new UnavailableExternalMarketplaceGateway(),
                ctx,
                _clock);

            var accIfood = (await hub.ConnectPartnerAccountAsync(manager, MarketplaceProvider.IFood, "iFood Matriz", "client-id-99", ifoodSecret)).Value;
            // Credencial persistida está cifrada em AES-GCM e nunca em texto puro
            Assert.StartsWith("v1:", accIfood.EncryptedCredentialsBlob, StringComparison.Ordinal);
            Assert.NotEqual(ifoodSecret, accIfood.EncryptedCredentialsBlob);
            Assert.Equal(ifoodSecret, _protector.Unprotect(accIfood.EncryptedCredentialsBlob));

            var bindIfood = (await hub.BindExternalStoreAsync(manager, accIfood.Id, "IF-MERCH-01", "Açaí Central - iFood")).Value;
            ifoodBindingId = bindIfood.Id;

            await hub.MapCatalogProductAsync(manager, ifoodBindingId, Guid.NewGuid(), "Açaí 500ml", "SKU-ACAI-500", 24.90m);

            var accAiq = (await hub.ConnectPartnerAccountAsync(manager, MarketplaceProvider.Aiqfome, "Aiqfome Matriz", "cli-aq", "segredo-aq")).Value;
            var bindAiq = (await hub.BindExternalStoreAsync(manager, accAiq.Id, "AQ-MERCH-01", "Açaí Central - Aiqfome")).Value;
            aiqfomeBindingId = bindAiq.Id;

            // Recebe webhook iFood assinado em HMAC e garante idempotência
            const string body = """{"orderId":"ORD-IF-900","displayId":"900","customerName":"Juliana","deliveryFee":5.0,"discounts":0.0,"totalAmount":54.80,"orderType":"DELIVERY","items":[{"externalCode":"SKU-ACAI-500","name":"Açaí 500ml","quantity":2,"unitPrice":24.90}]}""";
            var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ifoodSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

            var ev1 = (await hub.ReceiveAndNormalizeWebhookAsync(MarketplaceProvider.IFood, "EVT-IF-001", "IF-MERCH-01", "PLACED", body, sig)).Value;
            var ev2 = (await hub.ReceiveAndNormalizeWebhookAsync(MarketplaceProvider.IFood, "EVT-IF-001", "IF-MERCH-01", "PLACED", body, sig)).Value;

            Assert.Equal(ev1.Id, ev2.Id);
            Assert.Equal(MarketplaceOrderSyncStatus.Normalized, ev1.SyncStatus);
            Assert.Equal(54.80m, ev1.NormalizedTotal.Amount);
            Assert.True(ev1.SignatureVerified);

            // Tentativa de ação não suportada em Aiqfome (SyncItemPrice) falha explicitamente na matriz
            var unsup = await hub.ExecuteBindingActionAsync(manager, aiqfomeBindingId, MarketplaceActionKind.SyncItemPrice, "SKU-1", "{\"price\":20}");
            Assert.True(unsup.IsFailure);
            Assert.Equal("Marketplace.CapabilityUnsupported", unsup.Error.Code);

            // Chamada remota suportada sem credenciais externas reais falha explicitamente via UnavailableExternalMarketplaceGateway
            var offlineCall = await hub.ExecuteBindingActionAsync(manager, ifoodBindingId, MarketplaceActionKind.ConfirmOrder, "ORD-IF-900");
            Assert.True(offlineCall.IsFailure);
            Assert.Equal("Marketplace.ExternalCredentialsNotConfigured", offlineCall.Error.Code);
        }
    }
}
