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

public interface IDeliveryCourierRepository
{
    Task<DeliveryCourier?> FindByIdAsync(Guid courierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryCourier>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(DeliveryCourier courier, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDeliveryDispatchRunRepository
{
    Task<DeliveryDispatchRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryDispatchRun>> ListRecentAsync(int limit = 30, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryDispatchRun>> ListByCourierPeriodAsync(Guid courierId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
    Task AddAsync(DeliveryDispatchRun run, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICourierGpsPingRepository
{
    Task<IReadOnlyList<CourierGpsPing>> ListRecentByCourierAsync(Guid courierId, int limit, CancellationToken cancellationToken = default);
    Task AddAsync(CourierGpsPing ping, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record CourierGpsRealtimeEvent(
    Guid TenantId,
    Guid CourierId,
    Guid? ActiveDispatchRunId,
    double Latitude,
    double Longitude,
    decimal? SpeedKmh,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// Porta de notificação em tempo real de posição GPS do entregador (ex.: SignalR Hub por tenant).
/// </summary>
public interface ICourierGpsRealtimeNotifier
{
    Task NotifyLocationAsync(CourierGpsRealtimeEvent gpsEvent, CancellationToken cancellationToken = default);
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
