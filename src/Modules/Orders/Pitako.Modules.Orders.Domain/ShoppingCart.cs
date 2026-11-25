using System.Security.Cryptography;
using System.Text;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Orders.Domain;

public enum CartItemKind
{
    CatalogProduct = 1,
    CustomAcaiCup = 2,
    WeightedProduct = 3,
    Combo = 4
}

/// <summary>
/// Item individual do carrinho que preserva integralmente personalizações, escolhas,
/// observações e fingerprint canônico para impedir agrupamento incorreto (Seção 17).
/// </summary>
public sealed class ShoppingCartItem : Entity<Guid>, ITenantScopedEntity
{
    private ShoppingCartItem()
    {
        ItemTitle = string.Empty;
        CustomizationSummaryText = string.Empty;
        SelectionsPayloadJson = "{}";
        CustomizationFingerprint = string.Empty;
        UnitPrice = Money.Zero();
        LineSubtotal = Money.Zero();
    }

    internal ShoppingCartItem(
        Guid id,
        Guid tenantId,
        Guid shoppingCartId,
        CartItemKind itemKind,
        Guid? productId,
        Guid? categoryId,
        Guid? variationId,
        Guid? acaiCupSizeId,
        Guid? weighingRecordId,
        string itemTitle,
        string customizationSummaryText,
        string selectionsPayloadJson,
        string customizationFingerprint,
        Money unitPrice,
        int quantity,
        bool hasNonCombinablePromotion,
        string? customerNotes,
        DateTimeOffset addedAtUtc) : base(id)
    {
        TenantId = tenantId;
        ShoppingCartId = shoppingCartId;
        ItemKind = itemKind;
        ProductId = productId;
        CategoryId = categoryId;
        VariationId = variationId;
        AcaiCupSizeId = acaiCupSizeId;
        WeighingRecordId = weighingRecordId;
        ItemTitle = itemTitle;
        CustomizationSummaryText = customizationSummaryText;
        SelectionsPayloadJson = selectionsPayloadJson;
        CustomizationFingerprint = customizationFingerprint;
        UnitPrice = Money.Create(unitPrice.Amount, unitPrice.Currency).Value;
        Quantity = quantity;
        LineSubtotal = Money.Create(unitPrice.Amount * quantity, unitPrice.Currency).Value;
        HasNonCombinablePromotion = hasNonCombinablePromotion;
        CustomerNotes = customerNotes;
        AddedAtUtc = addedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid ShoppingCartId { get; private set; }
    public CartItemKind ItemKind { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? VariationId { get; private set; }
    public Guid? AcaiCupSizeId { get; private set; }
    public Guid? WeighingRecordId { get; private set; }
    public string ItemTitle { get; private set; }
    public string CustomizationSummaryText { get; private set; }
    public string SelectionsPayloadJson { get; private set; }
    public string CustomizationFingerprint { get; private set; }
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineSubtotal { get; private set; }
    public bool HasNonCombinablePromotion { get; private set; }
    public string? CustomerNotes { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }

    internal void UpdateQuantity(int newQuantity)
    {
        Quantity = newQuantity;
        LineSubtotal = UnitPrice.Multiply(newQuantity);
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em ShoppingCartItem.");
        TenantId = tenantId;
    }

    public static string ComputeFingerprint(
        CartItemKind kind,
        Guid? productId,
        Guid? variationId,
        Guid? acaiCupSizeId,
        Guid? weighingRecordId,
        string selectionsPayloadJson,
        string? customerNotes)
    {
        var canonical = $"{(int)kind}|{productId:N}|{variationId:N}|{acaiCupSizeId:N}|{weighingRecordId:N}|{selectionsPayloadJson}|{customerNotes?.Trim() ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Carrinho de compras multi-tenant com recálculo integral de preços/cupons no backend (Seção 17).
/// </summary>
public sealed class ShoppingCart : TenantScopedAggregateRoot
{
    private readonly List<ShoppingCartItem> _items = new();

    private ShoppingCart()
    {
        CartSessionKey = string.Empty;
        DeliveryFee = Money.Zero();
        ItemsSubtotal = Money.Zero();
        ItemsDiscount = Money.Zero();
        DeliveryDiscount = Money.Zero();
        TotalAmount = Money.Zero();
    }

    private ShoppingCart(
        Guid id,
        Guid tenantId,
        string cartSessionKey,
        string? customerIdentifier,
        DateTimeOffset expiresAtUtc) : base(id, tenantId)
    {
        CartSessionKey = cartSessionKey;
        CustomerIdentifier = customerIdentifier;
        DeliveryFee = Money.Zero();
        ItemsSubtotal = Money.Zero();
        ItemsDiscount = Money.Zero();
        DeliveryDiscount = Money.Zero();
        TotalAmount = Money.Zero();
        ExpiresAtUtc = expiresAtUtc;
    }

    public string CartSessionKey { get; private set; }
    public string? CustomerIdentifier { get; private set; }
    public string? AppliedCouponCode { get; private set; }
    public Money DeliveryFee { get; private set; }
    public Money ItemsSubtotal { get; private set; }
    public Money ItemsDiscount { get; private set; }
    public Money DeliveryDiscount { get; private set; }
    public Money TotalAmount { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public IReadOnlyCollection<ShoppingCartItem> Items => _items.AsReadOnly();

    public static Result<ShoppingCart> Create(
        Guid tenantId,
        string cartSessionKey,
        DateTimeOffset expiresAtUtc,
        string? customerIdentifier = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ShoppingCart>(Error.Validation("Cart.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(cartSessionKey))
            return Result.Failure<ShoppingCart>(Error.Validation("Cart.SessionRequired", "Identificador de sessão do carrinho é obrigatório."));

        return Result.Success(new ShoppingCart(
            Guid.NewGuid(),
            tenantId,
            cartSessionKey.Trim(),
            string.IsNullOrWhiteSpace(customerIdentifier) ? null : customerIdentifier.Trim().ToLowerInvariant(),
            expiresAtUtc));
    }

    public Result<ShoppingCartItem> AddItem(
        CartItemKind kind,
        Guid? productId,
        Guid? categoryId,
        Guid? variationId,
        Guid? acaiCupSizeId,
        Guid? weighingRecordId,
        string itemTitle,
        string customizationSummaryText,
        string selectionsPayloadJson,
        Money unitPrice,
        int quantity,
        bool hasNonCombinablePromotion,
        string? customerNotes,
        DateTimeOffset utcNow,
        bool forceSeparateLine = false)
    {
        if (quantity <= 0)
            return Result.Failure<ShoppingCartItem>(Error.Validation("Cart.InvalidQuantity", "Quantidade do item deve ser positiva."));
        if (string.IsNullOrWhiteSpace(itemTitle))
            return Result.Failure<ShoppingCartItem>(Error.Validation("Cart.TitleRequired", "Descrição do item é obrigatória."));

        ArgumentNullException.ThrowIfNull(unitPrice);

        var fingerprint = ShoppingCartItem.ComputeFingerprint(
            kind, productId, variationId, acaiCupSizeId, weighingRecordId, selectionsPayloadJson, customerNotes);

        // Pesagens únicas, linhas forçadas ou personalizações distintas nunca são agrupadas incorretamente.
        var canMergeWithExisting = !forceSeparateLine && !weighingRecordId.HasValue;
        if (canMergeWithExisting)
        {
            var existing = _items.FirstOrDefault(i => i.CustomizationFingerprint == fingerprint && i.UnitPrice.Amount == unitPrice.Amount);
            if (existing is not null)
            {
                existing.UpdateQuantity(existing.Quantity + quantity);
                RecalculateRawTotals();
                return Result.Success(existing);
            }
        }

        var item = new ShoppingCartItem(
            Guid.NewGuid(),
            TenantId,
            Id,
            kind,
            productId,
            categoryId,
            variationId,
            acaiCupSizeId,
            weighingRecordId,
            itemTitle.Trim(),
            customizationSummaryText,
            selectionsPayloadJson,
            fingerprint,
            unitPrice,
            quantity,
            hasNonCombinablePromotion,
            string.IsNullOrWhiteSpace(customerNotes) ? null : customerNotes.Trim(),
            utcNow);

        _items.Add(item);
        RecalculateRawTotals();
        return Result.Success(item);
    }

    public Result RemoveItem(Guid cartItemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == cartItemId);
        if (item is null)
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Item não encontrado no carrinho."));

        _items.Remove(item);
        RecalculateRawTotals();
        return Result.Success();
    }

    public Result UpdateItemQuantity(Guid cartItemId, int newQuantity)
    {
        var item = _items.FirstOrDefault(i => i.Id == cartItemId);
        if (item is null)
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Item não encontrado no carrinho."));

        if (item.WeighingRecordId.HasValue && newQuantity != 1)
            return Result.Failure(Error.Validation("Cart.WeightedQtyFixed", "Item pesado na balança possui quantidade fixa 1."));

        if (newQuantity <= 0)
            _items.Remove(item);
        else
            item.UpdateQuantity(newQuantity);

        RecalculateRawTotals();
        return Result.Success();
    }

    public void SetCustomerIdentifier(string? customerIdentifier) =>
        CustomerIdentifier = string.IsNullOrWhiteSpace(customerIdentifier) ? null : customerIdentifier.Trim().ToLowerInvariant();

    public void SetDeliveryFee(Money deliveryFee)
    {
        ArgumentNullException.ThrowIfNull(deliveryFee);
        DeliveryFee = Money.Create(deliveryFee.Amount, deliveryFee.Currency).Value;
        ApplyCalculatedDiscounts(AppliedCouponCode, ItemsDiscount, DeliveryDiscount);
    }

    public void ApplyCalculatedDiscounts(string? couponCode, Money itemsDiscount, Money deliveryDiscount)
    {
        AppliedCouponCode = string.IsNullOrWhiteSpace(couponCode) ? null : couponCode.Trim().ToUpperInvariant();
        ItemsDiscount = Money.Create(itemsDiscount.Amount, itemsDiscount.Currency).Value;
        DeliveryDiscount = Money.Create(deliveryDiscount.Amount, deliveryDiscount.Currency).Value;
        RecalculateRawTotals();
    }

    public void ClearCoupon()
    {
        AppliedCouponCode = null;
        ItemsDiscount = Money.Zero(DeliveryFee.Currency);
        DeliveryDiscount = Money.Zero(DeliveryFee.Currency);
        RecalculateRawTotals();
    }

    private void RecalculateRawTotals()
    {
        var currency = DeliveryFee.Currency;
        var subtotal = Money.Zero(currency);
        foreach (var item in _items)
            subtotal = subtotal.Add(item.LineSubtotal);

        ItemsSubtotal = subtotal;

        var cappedItemsDiscount = Math.Min(ItemsDiscount.Amount, ItemsSubtotal.Amount);
        if (cappedItemsDiscount != ItemsDiscount.Amount)
            ItemsDiscount = Money.Create(cappedItemsDiscount, currency).Value;

        var cappedDeliveryDiscount = Math.Min(DeliveryDiscount.Amount, DeliveryFee.Amount);
        if (cappedDeliveryDiscount != DeliveryDiscount.Amount)
            DeliveryDiscount = Money.Create(cappedDeliveryDiscount, currency).Value;

        var netItems = Math.Max(0m, ItemsSubtotal.Amount - ItemsDiscount.Amount);
        var netDelivery = Math.Max(0m, DeliveryFee.Amount - DeliveryDiscount.Amount);
        TotalAmount = Money.Create(netItems + netDelivery, currency).Value;
    }
}
