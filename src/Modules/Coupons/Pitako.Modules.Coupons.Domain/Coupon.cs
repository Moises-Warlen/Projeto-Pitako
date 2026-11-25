using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Coupons.Domain;

public enum CouponDiscountType
{
    Percentage = 1,
    FixedAmount = 2,
    FreeShipping = 3
}

public enum CouponTargetType
{
    Product = 1,
    Category = 2
}

public sealed record CouponBasketLine(
    Guid? ProductId,
    Guid? CategoryId,
    Money LineSubtotal,
    bool HasNonCombinablePromotion = false);

public sealed record CouponEvaluationResult(
    Guid CouponId,
    string CouponCode,
    CouponDiscountType DiscountType,
    Money ItemsDiscount,
    Money DeliveryDiscount,
    Money TotalDiscount,
    Money EligibleSubtotal);

/// <summary>
/// Regra de elegibilidade por produto ou categoria para cupons restritos (Seção 18).
/// </summary>
public sealed class CouponEligibilityRule : Entity<Guid>, ITenantScopedEntity
{
    private CouponEligibilityRule()
    {
    }

    internal CouponEligibilityRule(
        Guid id,
        Guid tenantId,
        Guid couponId,
        CouponTargetType targetType,
        Guid targetId) : base(id)
    {
        TenantId = tenantId;
        CouponId = couponId;
        TargetType = targetType;
        TargetId = targetId;
    }

    public Guid TenantId { get; private set; }
    public Guid CouponId { get; private set; }
    public CouponTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em CouponEligibilityRule.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Registro histórico imutável de utilização e eventual estorno de cupom (Seção 18).
/// </summary>
public sealed class CouponRedemption : Entity<Guid>, ITenantScopedEntity
{
    private CouponRedemption()
    {
        CustomerIdentifier = string.Empty;
        DiscountApplied = Money.Zero();
    }

    internal CouponRedemption(
        Guid id,
        Guid tenantId,
        Guid couponId,
        Guid orderId,
        string customerIdentifier,
        Money discountApplied,
        DateTimeOffset redeemedAtUtc) : base(id)
    {
        TenantId = tenantId;
        CouponId = couponId;
        OrderId = orderId;
        CustomerIdentifier = customerIdentifier;
        DiscountApplied = Money.Create(discountApplied.Amount, discountApplied.Currency).Value;
        RedeemedAtUtc = redeemedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid CouponId { get; private set; }
    public Guid OrderId { get; private set; }
    public string CustomerIdentifier { get; private set; }
    public Money DiscountApplied { get; private set; }
    public DateTimeOffset RedeemedAtUtc { get; private set; }
    public DateTimeOffset? ReversedAtUtc { get; private set; }
    public bool IsReversed => ReversedAtUtc.HasValue;

    internal void MarkReversed(DateTimeOffset reversedAtUtc)
    {
        ReversedAtUtc ??= reversedAtUtc;
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em CouponRedemption.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Cupom de desconto isolado por estabelecimento, com validade, pedido mínimo,
/// limites globais e por cliente, elegibilidade e histórico de uso (Seção 18).
/// </summary>
public sealed class Coupon : TenantScopedAggregateRoot
{
    private readonly List<CouponEligibilityRule> _eligibilityRules = new();
    private readonly List<CouponRedemption> _redemptions = new();

    private Coupon()
    {
        Code = string.Empty;
        MinimumOrderAmount = Money.Zero();
    }

    private Coupon(
        Guid id,
        Guid tenantId,
        string code,
        string? description,
        CouponDiscountType discountType,
        decimal discountValue,
        Money? maxDiscountAmount,
        Money minimumOrderAmount,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        int? maxTotalUses,
        int? maxUsesPerCustomer,
        bool canCombineWithPromotions) : base(id, tenantId)
    {
        Code = code;
        Description = description;
        DiscountType = discountType;
        DiscountValue = discountValue;
        MaxDiscountAmount = maxDiscountAmount;
        MinimumOrderAmount = minimumOrderAmount;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        MaxTotalUses = maxTotalUses;
        MaxUsesPerCustomer = maxUsesPerCustomer;
        CanCombineWithPromotions = canCombineWithPromotions;
        IsActive = true;
    }

    public string Code { get; private set; }
    public string? Description { get; private set; }
    public CouponDiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public Money? MaxDiscountAmount { get; private set; }
    public Money MinimumOrderAmount { get; private set; }
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset ValidUntilUtc { get; private set; }
    public int? MaxTotalUses { get; private set; }
    public int? MaxUsesPerCustomer { get; private set; }
    public bool CanCombineWithPromotions { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<CouponEligibilityRule> EligibilityRules => _eligibilityRules.AsReadOnly();
    public IReadOnlyCollection<CouponRedemption> Redemptions => _redemptions.AsReadOnly();

    public static string NormalizeCode(string? rawCode) =>
        string.IsNullOrWhiteSpace(rawCode) ? string.Empty : rawCode.Trim().Replace(" ", string.Empty).ToUpperInvariant();

    public static Result<Coupon> Create(
        Guid tenantId,
        string code,
        CouponDiscountType discountType,
        decimal discountValue,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        Money minimumOrderAmount,
        Money? maxDiscountAmount = null,
        int? maxTotalUses = null,
        int? maxUsesPerCustomer = null,
        bool canCombineWithPromotions = false,
        string? description = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Coupon>(Error.Validation("Coupon.TenantRequired", "TenantId é obrigatório."));

        var normalized = NormalizeCode(code);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 40)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidCode", "Código do cupom é obrigatório (até 40 caracteres)."));

        if (validUntilUtc <= validFromUtc)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidWindow", "Data final do cupom deve ser posterior à data inicial."));

        if (discountType == CouponDiscountType.Percentage && (discountValue <= 0m || discountValue > 100m))
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidPercentage", "Percentual do cupom deve estar entre 0 e 100."));

        if (discountType == CouponDiscountType.FixedAmount && discountValue <= 0m)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidFixedValue", "Valor fixo do cupom deve ser positivo."));

        if (discountType == CouponDiscountType.FreeShipping && discountValue < 0m)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidValue", "Valor do cupom inválido."));

        if (maxTotalUses.HasValue && maxTotalUses.Value <= 0)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidMaxTotalUses", "Limite global de usos deve ser positivo."));

        if (maxUsesPerCustomer.HasValue && maxUsesPerCustomer.Value <= 0)
            return Result.Failure<Coupon>(Error.Validation("Coupon.InvalidMaxPerCustomer", "Limite de usos por cliente deve ser positivo."));

        ArgumentNullException.ThrowIfNull(minimumOrderAmount);

        return Result.Success(new Coupon(
            Guid.NewGuid(),
            tenantId,
            normalized,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            discountType,
            discountType == CouponDiscountType.FreeShipping ? 0m : decimal.Round(discountValue, 2, MidpointRounding.ToEven),
            maxDiscountAmount,
            minimumOrderAmount,
            validFromUtc,
            validUntilUtc,
            maxTotalUses,
            maxUsesPerCustomer,
            canCombineWithPromotions));
    }

    public void AddEligibilityRule(CouponTargetType targetType, Guid targetId)
    {
        if (targetId == Guid.Empty || _eligibilityRules.Any(r => r.TargetType == targetType && r.TargetId == targetId))
            return;

        _eligibilityRules.Add(new CouponEligibilityRule(Guid.NewGuid(), TenantId, Id, targetType, targetId));
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public Result<CouponEvaluationResult> Evaluate(
        DateTimeOffset utcNow,
        string? customerIdentifier,
        IReadOnlyList<CouponBasketLine> basketLines,
        Money deliveryFee)
    {
        ArgumentNullException.ThrowIfNull(basketLines);
        ArgumentNullException.ThrowIfNull(deliveryFee);

        if (!IsActive)
            return Result.Failure<CouponEvaluationResult>(Error.Conflict("Coupon.Inactive", "Cupom desativado."));

        if (utcNow < ValidFromUtc || utcNow > ValidUntilUtc)
            return Result.Failure<CouponEvaluationResult>(Error.Validation("Coupon.ExpiredOrNotStarted", "Cupom fora do período de validade."));

        var activeTotalUses = _redemptions.Count(r => !r.IsReversed);
        if (MaxTotalUses.HasValue && activeTotalUses >= MaxTotalUses.Value)
            return Result.Failure<CouponEvaluationResult>(Error.Conflict("Coupon.TotalLimitReached", "Limite global de utilizações deste cupom foi atingido."));

        var normalizedCustomer = string.IsNullOrWhiteSpace(customerIdentifier) ? string.Empty : customerIdentifier.Trim().ToLowerInvariant();
        if (MaxUsesPerCustomer.HasValue)
        {
            if (string.IsNullOrWhiteSpace(normalizedCustomer))
                return Result.Failure<CouponEvaluationResult>(Error.Validation("Coupon.CustomerRequired", "Identificação do cliente é obrigatória para validar limite de uso deste cupom."));

            var customerUses = _redemptions.Count(r => !r.IsReversed && string.Equals(r.CustomerIdentifier, normalizedCustomer, StringComparison.Ordinal));
            if (customerUses >= MaxUsesPerCustomer.Value)
                return Result.Failure<CouponEvaluationResult>(Error.Conflict("Coupon.CustomerLimitReached", "Limite de utilizações por cliente atingido para este cupom."));
        }

        var currency = MinimumOrderAmount.Currency;
        var eligibleLines = basketLines.Where(IsLineEligible).ToList();
        if (eligibleLines.Count == 0)
            return Result.Failure<CouponEvaluationResult>(Error.Validation("Coupon.NoEligibleItems", "Nenhum item do carrinho é elegível para este cupom."));

        var eligibleSubtotal = Money.Zero(currency);
        foreach (var line in eligibleLines)
            eligibleSubtotal = eligibleSubtotal.Add(line.LineSubtotal);

        if (eligibleSubtotal.Amount < MinimumOrderAmount.Amount)
        {
            return Result.Failure<CouponEvaluationResult>(Error.Validation(
                "Coupon.MinimumOrderNotReached",
                $"Valor mínimo elegível de {MinimumOrderAmount} não atingido para usar este cupom."));
        }

        var itemsDiscount = Money.Zero(currency);
        var deliveryDiscount = Money.Zero(currency);

        switch (DiscountType)
        {
            case CouponDiscountType.Percentage:
                var pctAmount = decimal.Round(eligibleSubtotal.Amount * (DiscountValue / 100m), 2, MidpointRounding.ToEven);
                if (MaxDiscountAmount is not null && pctAmount > MaxDiscountAmount.Amount)
                    pctAmount = MaxDiscountAmount.Amount;
                pctAmount = Math.Min(pctAmount, eligibleSubtotal.Amount);
                itemsDiscount = Money.Create(pctAmount, currency).Value;
                break;

            case CouponDiscountType.FixedAmount:
                var fixedAmount = Math.Min(DiscountValue, eligibleSubtotal.Amount);
                itemsDiscount = Money.Create(fixedAmount, currency).Value;
                break;

            case CouponDiscountType.FreeShipping:
                var shipDiscount = deliveryFee.Amount;
                if (MaxDiscountAmount is not null && shipDiscount > MaxDiscountAmount.Amount)
                    shipDiscount = MaxDiscountAmount.Amount;
                deliveryDiscount = Money.Create(shipDiscount, currency).Value;
                break;
        }

        var totalDiscount = itemsDiscount.Add(deliveryDiscount);
        return Result.Success(new CouponEvaluationResult(
            Id,
            Code,
            DiscountType,
            itemsDiscount,
            deliveryDiscount,
            totalDiscount,
            eligibleSubtotal));
    }

    public Result<CouponRedemption> RecordRedemption(
        Guid orderId,
        string? customerIdentifier,
        Money discountApplied,
        DateTimeOffset redeemedAtUtc)
    {
        if (orderId == Guid.Empty)
            return Result.Failure<CouponRedemption>(Error.Validation("Coupon.OrderRequired", "OrderId é obrigatório para registrar uso do cupom."));

        ArgumentNullException.ThrowIfNull(discountApplied);

        var existing = _redemptions.FirstOrDefault(r => r.OrderId == orderId && !r.IsReversed);
        if (existing is not null)
            return Result.Success(existing);

        var normalizedCustomer = string.IsNullOrWhiteSpace(customerIdentifier)
            ? $"anon-{orderId:N}"
            : customerIdentifier.Trim().ToLowerInvariant();

        var redemption = new CouponRedemption(Guid.NewGuid(), TenantId, Id, orderId, normalizedCustomer, discountApplied, redeemedAtUtc);
        _redemptions.Add(redemption);
        return Result.Success(redemption);
    }

    public bool ReverseRedemptionForOrder(Guid orderId, DateTimeOffset reversedAtUtc)
    {
        var active = _redemptions.FirstOrDefault(r => r.OrderId == orderId && !r.IsReversed);
        if (active is null)
            return false;

        active.MarkReversed(reversedAtUtc);
        return true;
    }

    private bool IsLineEligible(CouponBasketLine line)
    {
        if (!CanCombineWithPromotions && line.HasNonCombinablePromotion)
            return false;

        if (_eligibilityRules.Count == 0)
            return true;

        return _eligibilityRules.Any(r =>
            (r.TargetType == CouponTargetType.Product && line.ProductId.HasValue && r.TargetId == line.ProductId.Value) ||
            (r.TargetType == CouponTargetType.Category && line.CategoryId.HasValue && r.TargetId == line.CategoryId.Value));
    }
}
