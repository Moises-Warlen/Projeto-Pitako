using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Coupons.Application.Services;

/// <summary>
/// Serviço de aplicação para gestão, validação no backend e histórico de cupons por estabelecimento (Seção 18).
/// </summary>
public sealed class CouponsManagementService : ICouponValidationService
{
    private readonly ICouponRepository _couponRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public CouponsManagementService(
        ICouponRepository couponRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _couponRepo = couponRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<Coupon>> CreateCouponAsync(
        User actor,
        string code,
        CouponDiscountType discountType,
        decimal discountValue,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        decimal minimumOrderAmount = 0m,
        decimal? maxDiscountAmount = null,
        int? maxTotalUses = null,
        int? maxUsesPerCustomer = null,
        bool canCombineWithPromotions = false,
        IEnumerable<(CouponTargetType TargetType, Guid TargetId)>? eligibilityRules = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Coupon>(guard.Error);

        var normalized = Coupon.NormalizeCode(code);
        var existing = await _couponRepo.FindByCodeAsync(normalized, cancellationToken);
        if (existing is not null)
            return Result.Failure<Coupon>(Error.Conflict("Coupon.DuplicateCode", $"Cupom '{normalized}' já existe neste estabelecimento."));

        var minMoneyRes = Money.Create(minimumOrderAmount);
        if (minMoneyRes.IsFailure)
            return Result.Failure<Coupon>(minMoneyRes.Error);

        Money? maxMoney = null;
        if (maxDiscountAmount.HasValue)
        {
            var maxRes = Money.Create(maxDiscountAmount.Value);
            if (maxRes.IsFailure)
                return Result.Failure<Coupon>(maxRes.Error);
            maxMoney = maxRes.Value;
        }

        var couponRes = Coupon.Create(
            guard.Value,
            normalized,
            discountType,
            discountValue,
            validFromUtc,
            validUntilUtc,
            minMoneyRes.Value,
            maxMoney,
            maxTotalUses,
            maxUsesPerCustomer,
            canCombineWithPromotions,
            description);

        if (couponRes.IsFailure)
            return couponRes;

        if (eligibilityRules is not null)
        {
            foreach (var (targetType, targetId) in eligibilityRules)
                couponRes.Value.AddEligibilityRule(targetType, targetId);
        }

        await _couponRepo.AddAsync(couponRes.Value, cancellationToken);
        await _couponRepo.SaveChangesAsync(cancellationToken);
        return couponRes;
    }

    public async Task<Result> DeactivateCouponAsync(User actor, Guid couponId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var coupon = await _couponRepo.FindByIdAsync(couponId, cancellationToken);
        if (coupon is null)
            return Result.Failure(Error.NotFound("Coupon.NotFound", "Cupom não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        coupon.Deactivate();
        await _couponRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReactivateCouponAsync(User actor, Guid couponId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var coupon = await _couponRepo.FindByIdAsync(couponId, cancellationToken);
        if (coupon is null)
            return Result.Failure(Error.NotFound("Coupon.NotFound", "Cupom não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        coupon.Activate();
        await _couponRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<Coupon>> UpdateCouponAsync(
        User actor,
        Guid couponId,
        CouponDiscountType discountType,
        decimal discountValue,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        decimal minimumOrderAmount = 0m,
        decimal? maxDiscountAmount = null,
        int? maxTotalUses = null,
        int? maxUsesPerCustomer = null,
        bool canCombineWithPromotions = false,
        string? description = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Coupon>(guard.Error);

        var coupon = await _couponRepo.FindByIdAsync(couponId, cancellationToken);
        if (coupon is null)
            return Result.Failure<Coupon>(Error.NotFound("Coupon.NotFound", "Cupom não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        var minRes = Money.Create(minimumOrderAmount);
        if (minRes.IsFailure)
            return Result.Failure<Coupon>(minRes.Error);

        Money? maxMoney = null;
        if (maxDiscountAmount.HasValue)
        {
            var maxRes = Money.Create(maxDiscountAmount.Value);
            if (maxRes.IsFailure)
                return Result.Failure<Coupon>(maxRes.Error);
            maxMoney = maxRes.Value;
        }

        var upd = coupon.UpdateDetails(discountType, discountValue, validFromUtc, validUntilUtc, minRes.Value, maxMoney, maxTotalUses, maxUsesPerCustomer, canCombineWithPromotions, description);
        if (upd.IsFailure)
            return Result.Failure<Coupon>(upd.Error);

        if (isActive) coupon.Activate();
        else coupon.Deactivate();

        await _couponRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(coupon);
    }

    public async Task<Result> DeleteCouponAsync(User actor, Guid couponId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var coupon = await _couponRepo.FindByIdAsync(couponId, cancellationToken);
        if (coupon is null)
            return Result.Failure(Error.NotFound("Coupon.NotFound", "Cupom não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        _couponRepo.Remove(coupon);
        await _couponRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CouponEvaluationResult>> EvaluateCouponAsync(
        string couponCode,
        string? customerIdentifier,
        IReadOnlyList<CouponBasketLine> basketLines,
        Money deliveryFee,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var normalized = Coupon.NormalizeCode(couponCode);
        if (string.IsNullOrWhiteSpace(normalized))
            return Result.Failure<CouponEvaluationResult>(Error.Validation("Coupon.EmptyCode", "Informe o código do cupom."));

        var coupon = await _couponRepo.FindByCodeAsync(normalized, cancellationToken);
        if (coupon is null)
            return Result.Failure<CouponEvaluationResult>(Error.NotFound("Coupon.NotFound", "Cupom inválido ou não encontrado neste estabelecimento."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        return coupon.Evaluate(_clock.UtcNow, customerIdentifier, basketLines, deliveryFee);
    }

    public async Task<Result<CouponEvaluationResult>> RedeemCouponForOrderAsync(
        string couponCode,
        Guid orderId,
        string? customerIdentifier,
        IReadOnlyList<CouponBasketLine> basketLines,
        Money deliveryFee,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var normalized = Coupon.NormalizeCode(couponCode);
        var coupon = await _couponRepo.FindByCodeAsync(normalized, cancellationToken);
        if (coupon is null)
            return Result.Failure<CouponEvaluationResult>(Error.NotFound("Coupon.NotFound", "Cupom inválido ou não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        var utcNow = _clock.UtcNow;

        var evalRes = coupon.Evaluate(utcNow, customerIdentifier, basketLines, deliveryFee);
        if (evalRes.IsFailure)
            return evalRes;

        var redeemRes = coupon.RecordRedemption(orderId, customerIdentifier, evalRes.Value.TotalDiscount, utcNow);
        if (redeemRes.IsFailure)
            return Result.Failure<CouponEvaluationResult>(redeemRes.Error);

        await _couponRepo.SaveChangesAsync(cancellationToken);
        return evalRes;
    }

    public async Task<Result<bool>> ReverseCouponRedemptionAsync(
        string couponCode,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var normalized = Coupon.NormalizeCode(couponCode);
        var coupon = await _couponRepo.FindByCodeAsync(normalized, cancellationToken);
        if (coupon is null)
            return Result.Success(false);

        TenantGuard.EnsureOwnership(_tenantContext, coupon);
        var reversed = coupon.ReverseRedemptionForOrder(orderId, _clock.UtcNow);
        if (reversed)
            await _couponRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(reversed);
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar cupons de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Coupons, action))
            return Result.Failure<Guid>(Error.Forbidden("Coupon.Forbidden", "Permissão insuficiente para configurar cupons."));

        return Result.Success(currentTenantId);
    }
}
