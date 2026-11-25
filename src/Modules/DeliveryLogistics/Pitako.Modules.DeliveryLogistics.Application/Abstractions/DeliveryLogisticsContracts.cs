using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DeliveryLogistics.Application.Abstractions;

public interface IDeliveryPolicyRepository
{
    Task<DeliveryPolicy?> FindByIdAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<DeliveryPolicy?> FindEffectivePolicyAsync(Guid? storeUnitId, CancellationToken cancellationToken = default);
    Task AddAsync(DeliveryPolicy policy, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato público de cotação de entrega própria para Carrinho e Checkout (Fases 11 e 13).
/// </summary>
public interface IDeliveryFreightQuoteService
{
    Task<Result<DeliveryFreightQuote>> QuoteFreightAsync(
        Guid? storeUnitId,
        DeliveryFreightQuoteRequest request,
        CancellationToken cancellationToken = default);
}
