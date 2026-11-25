using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Marketplaces.Domain;

public enum MarketplaceOrderSyncStatus
{
    Received = 1,
    Normalized = 2,
    AcknowledgedAtPartner = 3,
    DispatchedAtPartner = 4,
    Cancelled = 5,
    Failed = 6
}

public enum MarketplaceFulfillmentKind
{
    PartnerDelivery = 1,
    StoreOwnDelivery = 2,
    CounterPickup = 3
}

public sealed record MarketplaceNormalizedOrderItem(
    string ExternalSku,
    string Title,
    int Quantity,
    decimal UnitPriceAmount,
    string CustomizationSummary,
    string? Notes = null);

public sealed record MarketplaceNormalizedOrderPayload(
    MarketplaceProvider Provider,
    string ExternalOrderId,
    string ExternalShortCode,
    string ExternalMerchantId,
    string CustomerName,
    string? CustomerPhone,
    MarketplaceFulfillmentKind FulfillmentKind,
    string? DeliveryAddressSummary,
    IReadOnlyList<MarketplaceNormalizedOrderItem> Items,
    decimal DeliveryFeeAmount,
    decimal DiscountAmount,
    decimal TotalAmount);

/// <summary>
/// Evento inbound de webhook/polling de marketplace persistido com idempotência por provedor e loja (Seção 28).
/// </summary>
public sealed class MarketplaceInboundEvent : TenantScopedAggregateRoot
{
    private MarketplaceInboundEvent()
    {
        ExternalEventId = string.Empty;
        ExternalOrderId = string.Empty;
        ExternalMerchantId = string.Empty;
        EventType = string.Empty;
        RawPayloadJson = "{}";
        NormalizedItemsJson = "[]";
        NormalizedTotal = Money.Zero();
    }

    private MarketplaceInboundEvent(
        Guid id,
        Guid tenantId,
        MarketplaceProvider provider,
        Guid? storeBindingId,
        string externalEventId,
        string externalOrderId,
        string externalMerchantId,
        string eventType,
        string rawPayloadJson,
        bool signatureVerified,
        DateTimeOffset receivedAtUtc) : base(id, tenantId)
    {
        Provider = provider;
        StoreBindingId = storeBindingId;
        ExternalEventId = externalEventId;
        ExternalOrderId = externalOrderId;
        ExternalMerchantId = externalMerchantId;
        EventType = eventType;
        RawPayloadJson = rawPayloadJson;
        SignatureVerified = signatureVerified;
        SyncStatus = MarketplaceOrderSyncStatus.Received;
        NormalizedItemsJson = "[]";
        NormalizedTotal = Money.Zero();
        ReceivedAtUtc = receivedAtUtc;
    }

    public MarketplaceProvider Provider { get; private set; }
    public Guid? StoreBindingId { get; private set; }
    public string ExternalEventId { get; private set; }
    public string ExternalOrderId { get; private set; }
    public string ExternalMerchantId { get; private set; }
    public string EventType { get; private set; }
    public string RawPayloadJson { get; private set; }
    public bool SignatureVerified { get; private set; }
    public MarketplaceOrderSyncStatus SyncStatus { get; private set; }
    public string? NormalizedCustomerName { get; private set; }
    public MarketplaceFulfillmentKind? NormalizedFulfillment { get; private set; }
    public Money NormalizedTotal { get; private set; }
    public string NormalizedItemsJson { get; private set; }
    public Guid? InternalOrderId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public static Result<MarketplaceInboundEvent> Receive(
        Guid tenantId,
        MarketplaceProvider provider,
        Guid? storeBindingId,
        string externalEventId,
        string externalOrderId,
        string externalMerchantId,
        string eventType,
        string rawPayloadJson,
        bool signatureVerified,
        DateTimeOffset receivedAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<MarketplaceInboundEvent>(Error.Validation("Marketplace.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalEventId))
            return Result.Failure<MarketplaceInboundEvent>(Error.Validation("Marketplace.EventIdRequired", "ExternalEventId é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalMerchantId))
            return Result.Failure<MarketplaceInboundEvent>(Error.Validation("Marketplace.ExternalMerchantRequired", "ExternalMerchantId é obrigatório."));

        return Result.Success(new MarketplaceInboundEvent(
            Guid.NewGuid(),
            tenantId,
            provider,
            storeBindingId == Guid.Empty ? null : storeBindingId,
            externalEventId.Trim(),
            string.IsNullOrWhiteSpace(externalOrderId) ? externalEventId.Trim() : externalOrderId.Trim(),
            externalMerchantId.Trim(),
            string.IsNullOrWhiteSpace(eventType) ? "ORDER_EVENT" : eventType.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(rawPayloadJson) ? "{}" : rawPayloadJson,
            signatureVerified,
            receivedAtUtc));
    }

    public void MarkNormalized(
        MarketplaceNormalizedOrderPayload payload,
        string serializedItemsJson,
        DateTimeOffset processedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        NormalizedCustomerName = payload.CustomerName;
        NormalizedFulfillment = payload.FulfillmentKind;
        NormalizedTotal = Money.Create(Math.Max(0m, payload.TotalAmount)).Value;
        NormalizedItemsJson = string.IsNullOrWhiteSpace(serializedItemsJson) ? "[]" : serializedItemsJson;
        SyncStatus = MarketplaceOrderSyncStatus.Normalized;
        FailureReason = null;
        ProcessedAtUtc = processedAtUtc;
    }

    public void MarkDispatchedOrAcknowledged(MarketplaceOrderSyncStatus status, Guid? internalOrderId, DateTimeOffset processedAtUtc)
    {
        SyncStatus = status;
        if (internalOrderId.HasValue && internalOrderId.Value != Guid.Empty)
            InternalOrderId = internalOrderId;
        ProcessedAtUtc = processedAtUtc;
    }

    public void MarkFailed(string reason, DateTimeOffset processedAtUtc)
    {
        SyncStatus = MarketplaceOrderSyncStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Erro ao normalizar evento." : reason.Trim();
        ProcessedAtUtc = processedAtUtc;
    }
}

/// <summary>
/// Mapeamento de SKU/código entre um produto interno PITAKO e uma loja vinculada no marketplace (Seção 28).
/// </summary>
public sealed class MarketplaceCatalogMapping : TenantScopedAggregateRoot
{
    private MarketplaceCatalogMapping()
    {
        ExternalSkuCode = string.Empty;
        InternalItemTitle = string.Empty;
        LastSyncedPrice = Money.Zero();
    }

    private MarketplaceCatalogMapping(
        Guid id,
        Guid tenantId,
        Guid storeBindingId,
        MarketplaceProvider provider,
        Guid internalProductId,
        string internalItemTitle,
        string externalSkuCode,
        Money initialPrice) : base(id, tenantId)
    {
        StoreBindingId = storeBindingId;
        Provider = provider;
        InternalProductId = internalProductId;
        InternalItemTitle = internalItemTitle;
        ExternalSkuCode = externalSkuCode;
        LastSyncedPrice = Money.Create(initialPrice.Amount, initialPrice.Currency).Value;
        IsPausedAtPartner = false;
    }

    public Guid StoreBindingId { get; private set; }
    public MarketplaceProvider Provider { get; private set; }
    public Guid InternalProductId { get; private set; }
    public string InternalItemTitle { get; private set; }
    public string ExternalSkuCode { get; private set; }
    public bool IsPausedAtPartner { get; private set; }
    public Money LastSyncedPrice { get; private set; }
    public DateTimeOffset? LastSyncedAtUtc { get; private set; }

    public static Result<MarketplaceCatalogMapping> Create(
        Guid tenantId,
        MarketplaceStoreBinding binding,
        Guid internalProductId,
        string internalItemTitle,
        string externalSkuCode,
        decimal currentPrice)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (tenantId == Guid.Empty)
            return Result.Failure<MarketplaceCatalogMapping>(Error.Validation("Marketplace.TenantRequired", "TenantId é obrigatório."));
        if (binding.TenantId != tenantId)
            return Result.Failure<MarketplaceCatalogMapping>(Error.Forbidden("Marketplace.CrossTenantBinding", "Loja vinculada pertence a outro estabelecimento."));
        if (internalProductId == Guid.Empty)
            return Result.Failure<MarketplaceCatalogMapping>(Error.Validation("Marketplace.ProductRequired", "Produto interno é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalSkuCode))
            return Result.Failure<MarketplaceCatalogMapping>(Error.Validation("Marketplace.SkuRequired", "Código SKU externo é obrigatório."));

        var priceRes = Money.Create(Math.Max(0m, currentPrice));
        if (priceRes.IsFailure)
            return Result.Failure<MarketplaceCatalogMapping>(priceRes.Error);

        return Result.Success(new MarketplaceCatalogMapping(
            Guid.NewGuid(),
            tenantId,
            binding.Id,
            binding.Provider,
            internalProductId,
            string.IsNullOrWhiteSpace(internalItemTitle) ? externalSkuCode.Trim() : internalItemTitle.Trim(),
            externalSkuCode.Trim(),
            priceRes.Value));
    }

    public void RecordAvailabilitySync(bool isPaused, DateTimeOffset syncedAtUtc)
    {
        IsPausedAtPartner = isPaused;
        LastSyncedAtUtc = syncedAtUtc;
    }

    public void RecordPriceSync(Money newPrice, DateTimeOffset syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        LastSyncedPrice = Money.Create(newPrice.Amount, newPrice.Currency).Value;
        LastSyncedAtUtc = syncedAtUtc;
    }
}
