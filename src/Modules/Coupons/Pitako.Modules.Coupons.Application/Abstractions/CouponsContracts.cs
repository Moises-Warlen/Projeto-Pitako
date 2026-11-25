using Pitako.Modules.Coupons.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Coupons.Application.Abstractions;

public interface ICouponRepository
{
    Task<Coupon?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Coupon?> FindByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Coupon>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato público para validação, resgate e estorno de cupons pelo Carrinho e Pedidos (Fases 11 e 13).
/// </summary>
public interface ICouponValidationService
{
    Task<Result<CouponEvaluationResult>> EvaluateCouponAsync(
        string couponCode,
        string? customerIdentifier,
        IReadOnlyList<CouponBasketLine> basketLines,
        Money deliveryFee,
        CancellationToken cancellationToken = default);

    Task<Result<CouponEvaluationResult>> RedeemCouponForOrderAsync(
        string couponCode,
        Guid orderId,
        string? customerIdentifier,
        IReadOnlyList<CouponBasketLine> basketLines,
        Money deliveryFee,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ReverseCouponRedemptionAsync(
        string couponCode,
        Guid orderId,
        CancellationToken cancellationToken = default);
}
