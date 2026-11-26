using Pitako.Modules.Orders.Domain;

namespace Pitako.Modules.Orders.Application.Abstractions;

public interface IShoppingCartRepository
{
    Task<ShoppingCart?> FindBySessionKeyAsync(string cartSessionKey, CancellationToken cancellationToken = default);
    void Remove(ShoppingCart cart);
    Task AddAsync(ShoppingCart cart, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOrderRepository
{
    Task<Order?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Order?> FindByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<Order?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListKanbanActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetProductSalesCountsAsync(CancellationToken cancellationToken = default);
    Task<bool> HasProductReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record OrderRealtimeEvent(
    Guid TenantId,
    Guid OrderId,
    string OrderNumber,
    string EventType,
    OrderStatus Status,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Porta de publicação em tempo real por tenant para o painel Kanban de pedidos (ex.: SignalR Hub).
/// </summary>
public interface IOrderRealtimeNotifier
{
    Task NotifyAsync(OrderRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default);
}
