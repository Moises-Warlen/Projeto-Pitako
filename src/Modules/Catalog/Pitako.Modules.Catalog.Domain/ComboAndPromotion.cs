using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Domain;

public sealed class ComboItem : Entity<Guid>
{
    private ComboItem()
    {
    }

    internal ComboItem(Guid id, Guid tenantId, Guid comboId, Guid productId, int quantity, Guid? variationId, string? notes)
        : base(id)
    {
        TenantId = tenantId;
        ComboId = comboId;
        ProductId = productId;
        Quantity = quantity;
        VariationId = variationId;
        Notes = notes;
    }

    public Guid TenantId { get; private set; }
    public Guid ComboId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public Guid? VariationId { get; private set; }
    public string? Notes { get; private set; }
}

/// <summary>
/// Combo comercial composto por múltiplos produtos do catálogo da loja.
/// </summary>
public sealed class Combo : TenantScopedAggregateRoot
{
    private readonly List<ComboItem> _items = new();

    private Combo()
    {
        Name = string.Empty;
        ComboPrice = Money.Zero();
    }

    private Combo(
        Guid id,
        Guid tenantId,
        string name,
        string? description,
        Money comboPrice,
        string? primaryImageUrl,
        bool isFeatured,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        Description = description;
        ComboPrice = comboPrice;
        PrimaryImageUrl = primaryImageUrl;
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
        Status = CatalogItemStatus.Active;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Money ComboPrice { get; private set; }
    public string? PrimaryImageUrl { get; private set; }
    public CatalogItemStatus Status { get; private set; }
    public bool IsFeatured { get; private set; }
    public int DisplayOrder { get; private set; }

    public IReadOnlyCollection<ComboItem> Items => _items.AsReadOnly();

    public static Result<Combo> Create(
        Guid tenantId,
        string name,
        Money comboPrice,
        string? description = null,
        string? primaryImageUrl = null,
        bool isFeatured = false,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Combo>(Error.Validation("Combo.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Combo>(Error.Validation("Combo.NameRequired", "Nome do combo é obrigatório."));

        ArgumentNullException.ThrowIfNull(comboPrice);

        return Result.Success(new Combo(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            comboPrice,
            string.IsNullOrWhiteSpace(primaryImageUrl) ? null : primaryImageUrl.Trim(),
            isFeatured,
            displayOrder));
    }

    public Result UpdateDetails(
        string name,
        Money comboPrice,
        string? description,
        string? primaryImageUrl,
        bool isFeatured,
        int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Combo.NameRequired", "Nome do combo é obrigatório."));
        ArgumentNullException.ThrowIfNull(comboPrice);

        Name = name.Trim();
        ComboPrice = comboPrice;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PrimaryImageUrl = string.IsNullOrWhiteSpace(primaryImageUrl) ? null : primaryImageUrl.Trim();
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
        return Result.Success();
    }

    public void ClearItems() => _items.Clear();

    public Result AddItem(Guid productId, int quantity, Guid? variationId = null, string? notes = null)
    {
        if (productId == Guid.Empty)
            return Result.Failure(Error.Validation("Combo.ProductRequired", "Produto do combo é obrigatório."));

        if (quantity <= 0)
            return Result.Failure(Error.Validation("Combo.InvalidQty", "Quantidade do item no combo deve ser positiva."));

        _items.Add(new ComboItem(Guid.NewGuid(), TenantId, Id, productId, quantity, variationId, notes?.Trim()));
        return Result.Success();
    }

    public void Pause() => Status = CatalogItemStatus.Paused;
    public void Reactivate() => Status = CatalogItemStatus.Active;

    public bool CanReceiveOrders(IReadOnlyDictionary<Guid, Product> tenantProducts)
    {
        if (Status != CatalogItemStatus.Active || _items.Count == 0)
            return false;

        // Combo não pode ser pedido se algum componente estiver pausado/arquivado ou ausente.
        return _items.All(i => tenantProducts.TryGetValue(i.ProductId, out var prod) && prod.CanReceiveOrders());
    }
}

public enum PromotionDiscountType
{
    Percentage = 1,
    FixedAmount = 2,
    PromotionalFixedPrice = 3
}

public enum PromotionTargetType
{
    Product = 1,
    Category = 2
}

public sealed class PromotionTarget : Entity<Guid>
{
    private PromotionTarget()
    {
    }

    internal PromotionTarget(Guid id, Guid tenantId, Guid promotionId, PromotionTargetType targetType, Guid targetId)
        : base(id)
    {
        TenantId = tenantId;
        PromotionId = promotionId;
        TargetType = targetType;
        TargetId = targetId;
    }

    public Guid TenantId { get; private set; }
    public Guid PromotionId { get; private set; }
    public PromotionTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
}

/// <summary>
/// Promoção configurada pela loja com janela temporal e alvo por produto ou categoria.
/// </summary>
public sealed class Promotion : TenantScopedAggregateRoot
{
    private readonly List<PromotionTarget> _targets = new();

    private Promotion()
    {
        Name = string.Empty;
    }

    private Promotion(
        Guid id,
        Guid tenantId,
        string name,
        PromotionDiscountType discountType,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        bool canCombineWithCoupons) : base(id, tenantId)
    {
        Name = name;
        DiscountType = discountType;
        DiscountValue = discountValue;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CanCombineWithCoupons = canCombineWithCoupons;
        IsActive = true;
    }

    public string Name { get; private set; }
    public PromotionDiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public bool CanCombineWithCoupons { get; private set; }

    public IReadOnlyCollection<PromotionTarget> Targets => _targets.AsReadOnly();

    public static Result<Promotion> Create(
        Guid tenantId,
        string name,
        PromotionDiscountType discountType,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        bool canCombineWithCoupons = false)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Promotion>(Error.Validation("Promotion.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Promotion>(Error.Validation("Promotion.NameRequired", "Nome da promoção é obrigatório."));

        if (endsAtUtc <= startsAtUtc)
            return Result.Failure<Promotion>(Error.Validation("Promotion.InvalidPeriod", "Data final deve ser posterior à data inicial."));

        if (discountValue < 0m || (discountType == PromotionDiscountType.Percentage && discountValue > 100m))
            return Result.Failure<Promotion>(Error.Validation("Promotion.InvalidDiscount", "Valor promocional inválido."));

        return Result.Success(new Promotion(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            discountType,
            discountValue,
            startsAtUtc,
            endsAtUtc,
            canCombineWithCoupons));
    }

    public Result Update(
        string name,
        PromotionDiscountType discountType,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        bool canCombineWithCoupons)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Promotion.NameRequired", "Nome da promoção é obrigatório."));
        if (endsAtUtc <= startsAtUtc)
            return Result.Failure(Error.Validation("Promotion.InvalidPeriod", "Data final deve ser posterior à data inicial."));
        if (discountValue < 0m || (discountType == PromotionDiscountType.Percentage && discountValue > 100m))
            return Result.Failure(Error.Validation("Promotion.InvalidDiscount", "Valor promocional inválido."));

        Name = name.Trim();
        DiscountType = discountType;
        DiscountValue = discountValue;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CanCombineWithCoupons = canCombineWithCoupons;
        return Result.Success();
    }

    public void ClearTargets() => _targets.Clear();

    public void AddTarget(PromotionTargetType type, Guid targetId)
    {
        if (targetId == Guid.Empty || _targets.Any(t => t.TargetType == type && t.TargetId == targetId))
            return;

        _targets.Add(new PromotionTarget(Guid.NewGuid(), TenantId, Id, type, targetId));
    }

    public bool IsActiveAt(DateTimeOffset utcNow) =>
        IsActive && StartsAtUtc <= utcNow && EndsAtUtc >= utcNow;

    public bool AppliesTo(Product product) =>
        _targets.Any(t =>
            (t.TargetType == PromotionTargetType.Product && t.TargetId == product.Id) ||
            (t.TargetType == PromotionTargetType.Category && t.TargetId == product.CategoryId));

    public Money CalculatePromotionalPrice(Money originalPrice)
    {
        ArgumentNullException.ThrowIfNull(originalPrice);

        var resultingAmount = DiscountType switch
        {
            PromotionDiscountType.Percentage => originalPrice.Amount * (1m - (DiscountValue / 100m)),
            PromotionDiscountType.FixedAmount => originalPrice.Amount - DiscountValue,
            PromotionDiscountType.PromotionalFixedPrice => Math.Min(originalPrice.Amount, DiscountValue),
            _ => originalPrice.Amount
        };

        return Money.Create(Math.Max(0m, resultingAmount), originalPrice.Currency).Value;
    }

    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
