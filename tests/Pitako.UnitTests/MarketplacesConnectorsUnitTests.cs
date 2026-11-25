using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Connectors;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.UnitTests;

internal sealed class StubSuccessRemoteGateway : IMarketplaceRemoteGateway
{
    public Task<Result<string>> ExecuteRemoteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success("{\"status\":\"ok\"}"));
}

public sealed class MarketplacesConnectorsUnitTests
{
    [Fact]
    public void MarketplaceConnectorRegistry_ShouldSupportAllSevenProviders_WithDistinctWebhookSchemasAndHmac()
    {
        var registry = new MarketplaceConnectorRegistry();
        var capabilities = registry.ListAllCapabilities();

        Assert.Equal(7, capabilities.Count);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.IFood);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.Rappi);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.Aiqfome);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.DeliveryMuch);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.Food99);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.Keeta);
        Assert.Contains(capabilities, c => c.Provider == MarketplaceProvider.FoodToSave);

        var samples = new Dictionary<MarketplaceProvider, string>
        {
            [MarketplaceProvider.IFood] = """{"orderId":"IF-10","displayId":"1010","customerName":"Ana","deliveryFee":6.0,"discounts":2.0,"totalAmount":44.0,"orderType":"DELIVERY","items":[{"externalCode":"SKU-1","name":"Açaí 500ml","quantity":2,"unitPrice":20.0}]}""",
            [MarketplaceProvider.Rappi] = """{"order_id":"RP-20","short_code":"2020","client_name":"Bruno","delivery_fee":5.0,"total_value":35.0,"delivery_method":"TAKEOUT","items":[{"sku":"SKU-2","name":"Coxinha","units":3,"price":10.0}]}""",
            [MarketplaceProvider.Aiqfome] = """{"pedido_id":"AQ-30","codigo":"3030","cliente_nome":"Carla","taxa_entrega":4.0,"valor_total":24.0,"itens":[{"codigo_pdv":"SKU-3","nome":"Suco","quantidade":2,"valor_unitario":10.0}]}""",
            [MarketplaceProvider.DeliveryMuch] = """{"orderCode":"DM-40","shortCode":"4040","customer":"Diego","shipping":7.0,"totalValue":47.0,"products":[{"code":"SKU-4","title":"Burger","qty":2,"unitValue":20.0}]}""",
            [MarketplaceProvider.Food99] = """{"orderNo":"99F-50","pickupCode":"5050","buyerName":"Eva","freightAmount":3.0,"payAmount":28.0,"dishes":[{"dishCode":"SKU-5","dishName":"Tigela","count":1,"price":25.0}]}""",
            [MarketplaceProvider.Keeta] = """{"orderSn":"KT-60","daySeq":"6060","recipientName":"Fábio","deliveryAmount":5.0,"orderAmount":35.0,"skuList":[{"outerSkuId":"SKU-6","skuName":"Combo","quantity":1,"price":30.0}]}""",
            [MarketplaceProvider.FoodToSave] = """{"bagOrderCode":"FTS-70","pickupPin":"7070","consumerName":"Gabi","totalAmount":19.90,"pickupType":"PICKUP","bags":[{"bagSku":"BAG-M","bagTitle":"Sacola Surpresa M","quantity":1,"bagPrice":19.90}]}"""
        };

        foreach (var (provider, json) in samples)
        {
            var connector = registry.GetRequired(provider);
            var norm = connector.NormalizeOrderWebhook(json, $"MERCH-{provider}");
            Assert.True(norm.IsSuccess, $"Falha ao normalizar {provider}: {norm.Error.Message}");
            Assert.Single(norm.Value.Items);
        }

        // Validação HMAC em provedor que suporta assinatura (iFood)
        var ifood = registry.GetRequired(MarketplaceProvider.IFood);
        const string secret = "segredo-ifood-loja";
        var payload = samples[MarketplaceProvider.IFood];
        var sig = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        Assert.True(ifood.VerifyWebhookSignature(payload, sig, secret));
        Assert.False(ifood.VerifyWebhookSignature(payload, "sha256=invalida", secret));
    }

    [Fact]
    public async Task MarketplaceConnector_ShouldBlockUnsupportedPartnerActionsExplicitly()
    {
        var tenantId = Guid.NewGuid();
        var registry = new MarketplaceConnectorRegistry();
        var gateway = new StubSuccessRemoteGateway();

        // Aiqfome não suporta SyncItemPrice na matriz de capacidades
        var accAiqfome = MarketplacePartnerAccount.Create(tenantId, MarketplaceProvider.Aiqfome, "Aiqfome Centro", "cli-1", "v1:cipher").Value;
        var bindAiqfome = MarketplaceStoreBinding.Create(tenantId, accAiqfome, "LOJA-AQ-1", "Açaí Centro").Value;

        var unsupported = await registry.GetRequired(MarketplaceProvider.Aiqfome)
            .ExecuteActionAsync(accAiqfome, bindAiqfome, MarketplaceActionKind.SyncItemPrice, "SKU-1", "{}", gateway);

        Assert.True(unsupported.IsFailure);
        Assert.Equal("Marketplace.CapabilityUnsupported", unsupported.Error.Code);

        // Delivery Much não suporta ToggleStoreOpenClose
        var accDm = MarketplacePartnerAccount.Create(tenantId, MarketplaceProvider.DeliveryMuch, "DM Centro", "cli-2", "v1:cipher").Value;
        var bindDm = MarketplaceStoreBinding.Create(tenantId, accDm, "LOJA-DM-1", "Açaí DM").Value;

        var dmUnsupported = await registry.GetRequired(MarketplaceProvider.DeliveryMuch)
            .ExecuteActionAsync(accDm, bindDm, MarketplaceActionKind.ToggleStoreOpenClose, null, "{}", gateway);

        Assert.True(dmUnsupported.IsFailure);
        Assert.Equal("Marketplace.CapabilityUnsupported", dmUnsupported.Error.Code);
    }
}
