using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Marketplaces.Application.Connectors;

internal sealed record PartnerJsonSchemaMap(
    string OrderIdKey,
    string ShortCodeKey,
    string CustomerNameKey,
    string ItemsArrayKey,
    string ItemSkuKey,
    string ItemTitleKey,
    string ItemQtyKey,
    string ItemUnitPriceKey,
    string TotalAmountKey,
    string? DeliveryFeeKey = null,
    string? DiscountKey = null,
    string? FulfillmentKey = null);

/// <summary>
/// Base compartilhada dos conectores de marketplace: valida capacidades reais antes de chamar o gateway,
/// autentica assinatura HMAC quando suportada pelo parceiro e normaliza payloads heterogêneos para o núcleo PITAKO.
/// </summary>
public abstract class MarketplaceConnectorBase : IMarketplaceConnector
{
    private readonly PartnerJsonSchemaMap _schema;

    private protected MarketplaceConnectorBase(MarketplaceProvider provider, PartnerJsonSchemaMap schema)
    {
        Provider = provider;
        Capabilities = MarketplaceCapabilityMatrix.GetFor(provider);
        _schema = schema;
    }

    public MarketplaceProvider Provider { get; }
    public MarketplaceCapabilities Capabilities { get; }

    public virtual bool VerifyWebhookSignature(string rawBody, string? signatureHeader, string secretKey)
    {
        if (!Capabilities.SupportsWebhookHmacSignature)
            return true;

        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secretKey))
            return false;

        var keyBytes = Encoding.UTF8.GetBytes(secretKey.Trim());
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody ?? string.Empty);
        var computed = HMACSHA256.HashData(keyBytes, bodyBytes);
        var expectedHex = Convert.ToHexString(computed).ToLowerInvariant();
        var cleanedHeader = signatureHeader.Trim().Replace("sha256=", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(cleanedHeader.Length == expectedHex.Length ? cleanedHeader : new string('0', expectedHex.Length)));
    }

    public Result<MarketplaceNormalizedOrderPayload> NormalizeOrderWebhook(string rawBody, string externalMerchantId)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return Result.Failure<MarketplaceNormalizedOrderPayload>(Error.Validation("Marketplace.EmptyPayload", "Payload de webhook vazio."));

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var orderId = ReadString(root, _schema.OrderIdKey) ?? ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(orderId))
                return Result.Failure<MarketplaceNormalizedOrderPayload>(Error.Validation("Marketplace.OrderIdMissing", $"Campo '{_schema.OrderIdKey}' ausente no payload {Capabilities.DisplayName}."));

            var shortCode = ReadString(root, _schema.ShortCodeKey) ?? orderId;
            var customerName = ReadString(root, _schema.CustomerNameKey) ?? $"Cliente {Capabilities.DisplayName}";
            var deliveryFee = _schema.DeliveryFeeKey is not null ? ReadDecimal(root, _schema.DeliveryFeeKey) : 0m;
            var discount = _schema.DiscountKey is not null ? ReadDecimal(root, _schema.DiscountKey) : 0m;
            var fulfillmentRaw = _schema.FulfillmentKey is not null ? ReadString(root, _schema.FulfillmentKey) : null;
            var fulfillment = ResolveFulfillment(fulfillmentRaw);

            var items = new List<MarketplaceNormalizedOrderItem>();
            if (root.TryGetProperty(_schema.ItemsArrayKey, out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in itemsElem.EnumerateArray())
                {
                    var sku = ReadString(elem, _schema.ItemSkuKey) ?? "SEM-SKU";
                    var title = ReadString(elem, _schema.ItemTitleKey) ?? "Item";
                    var qty = Math.Max(1, (int)ReadDecimal(elem, _schema.ItemQtyKey, defaultValue: 1m));
                    var unitPrice = Math.Max(0m, ReadDecimal(elem, _schema.ItemUnitPriceKey));
                    var notes = ReadString(elem, "notes") ?? ReadString(elem, "observations");
                    var custom = ReadString(elem, "customization") ?? "Padrão";

                    items.Add(new MarketplaceNormalizedOrderItem(sku, title, qty, unitPrice, custom, notes));
                }
            }

            if (items.Count == 0)
                return Result.Failure<MarketplaceNormalizedOrderPayload>(Error.Validation("Marketplace.ItemsMissing", "Pedido recebido sem itens válidos."));

            var computedTotal = items.Sum(i => i.UnitPriceAmount * i.Quantity) + deliveryFee - discount;
            var declaredTotal = ReadDecimal(root, _schema.TotalAmountKey, defaultValue: computedTotal);

            return Result.Success(new MarketplaceNormalizedOrderPayload(
                Provider,
                orderId,
                shortCode,
                externalMerchantId.Trim(),
                customerName,
                ReadString(root, "customerPhone"),
                fulfillment,
                ReadString(root, "deliveryAddress"),
                items,
                deliveryFee,
                discount,
                Math.Max(0m, declaredTotal)));
        }
        catch (JsonException ex)
        {
            return Result.Failure<MarketplaceNormalizedOrderPayload>(Error.Validation("Marketplace.InvalidJson", $"JSON inválido para {Capabilities.DisplayName}: {ex.Message}"));
        }
    }

    public async Task<Result<string>> ExecuteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        IMarketplaceRemoteGateway remoteGateway,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(remoteGateway);

        if (!Capabilities.SupportsAction(action))
        {
            return Result.Failure<string>(Error.Validation(
                "Marketplace.CapabilityUnsupported",
                $"O conector {Capabilities.DisplayName} não suporta a operação '{action}' em sua API oficial."));
        }

        if (binding.Status == MarketplaceBindingStatus.Disconnected)
        {
            return Result.Failure<string>(Error.Conflict(
                "Marketplace.BindingDisconnected",
                $"A loja '{binding.ExternalStoreName}' ({Capabilities.DisplayName}) está desconectada."));
        }

        return await remoteGateway.ExecuteRemoteActionAsync(account, binding, action, targetExternalId, payloadJson, cancellationToken);
    }

    private static MarketplaceFulfillmentKind ResolveFulfillment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return MarketplaceFulfillmentKind.PartnerDelivery;

        return raw.Trim().ToUpperInvariant() switch
        {
            "TAKEOUT" or "PICKUP" or "COUNTER_PICKUP" => MarketplaceFulfillmentKind.CounterPickup,
            "STORE_DELIVERY" or "OWN_DELIVERY" or "MERCHANT" => MarketplaceFulfillmentKind.StoreOwnDelivery,
            _ => MarketplaceFulfillmentKind.PartnerDelivery
        };
    }

    private static string? ReadString(JsonElement elem, string key)
    {
        if (!elem.TryGetProperty(key, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }

    private static decimal ReadDecimal(JsonElement elem, string key, decimal defaultValue = 0m)
    {
        if (!elem.TryGetProperty(key, out var prop))
            return defaultValue;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var dec))
            return dec;

        if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return defaultValue;
    }
}

public sealed class IFoodConnector() : MarketplaceConnectorBase(MarketplaceProvider.IFood, new(
    "orderId", "displayId", "customerName", "items", "externalCode", "name", "quantity", "unitPrice", "totalAmount", "deliveryFee", "discounts", "orderType"));

public sealed class RappiConnector() : MarketplaceConnectorBase(MarketplaceProvider.Rappi, new(
    "order_id", "short_code", "client_name", "items", "sku", "name", "units", "price", "total_value", "delivery_fee", "discount_value", "delivery_method"));

public sealed class AiqfomeConnector() : MarketplaceConnectorBase(MarketplaceProvider.Aiqfome, new(
    "pedido_id", "codigo", "cliente_nome", "itens", "codigo_pdv", "nome", "quantidade", "valor_unitario", "valor_total", "taxa_entrega", "desconto", "modalidade"));

public sealed class DeliveryMuchConnector() : MarketplaceConnectorBase(MarketplaceProvider.DeliveryMuch, new(
    "orderCode", "shortCode", "customer", "products", "code", "title", "qty", "unitValue", "totalValue", "shipping", "voucher", "fulfillment"));

public sealed class Food99Connector() : MarketplaceConnectorBase(MarketplaceProvider.Food99, new(
    "orderNo", "pickupCode", "buyerName", "dishes", "dishCode", "dishName", "count", "price", "payAmount", "freightAmount", "promoAmount", "deliverType"));

public sealed class KeetaConnector() : MarketplaceConnectorBase(MarketplaceProvider.Keeta, new(
    "orderSn", "daySeq", "recipientName", "skuList", "outerSkuId", "skuName", "quantity", "price", "orderAmount", "deliveryAmount", "discountAmount", "deliveryMode"));

public sealed class FoodToSaveConnector() : MarketplaceConnectorBase(MarketplaceProvider.FoodToSave, new(
    "bagOrderCode", "pickupPin", "consumerName", "bags", "bagSku", "bagTitle", "quantity", "bagPrice", "totalAmount", "deliveryFee", "discount", "pickupType"));

/// <summary>
/// Registro central dos conectores oficiais para os 7 marketplaces configurados no Hub.
/// </summary>
public sealed class MarketplaceConnectorRegistry
{
    private readonly IReadOnlyDictionary<MarketplaceProvider, IMarketplaceConnector> _connectors;

    public MarketplaceConnectorRegistry(IEnumerable<IMarketplaceConnector>? customConnectors = null)
    {
        var defaults = new IMarketplaceConnector[]
        {
            new IFoodConnector(),
            new RappiConnector(),
            new AiqfomeConnector(),
            new DeliveryMuchConnector(),
            new Food99Connector(),
            new KeetaConnector(),
            new FoodToSaveConnector()
        };

        var dict = defaults.ToDictionary(c => c.Provider);
        if (customConnectors is not null)
        {
            foreach (var c in customConnectors)
                dict[c.Provider] = c;
        }

        _connectors = dict;
    }

    public IMarketplaceConnector GetRequired(MarketplaceProvider provider) =>
        _connectors.TryGetValue(provider, out var connector)
            ? connector
            : throw new InvalidOperationException($"Conector não registrado para o provedor {provider}.");

    public IReadOnlyList<MarketplaceCapabilities> ListAllCapabilities() =>
        _connectors.Values.Select(c => c.Capabilities).OrderBy(c => (int)c.Provider).ToList();
}
