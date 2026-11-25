using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;

namespace Pitako.Modules.Orders.Infrastructure.Persistence;

public sealed class EfShoppingCartRepository(OrdersDbContext db) : IShoppingCartRepository
{
    public Task<ShoppingCart?> FindBySessionKeyAsync(string cartSessionKey, CancellationToken cancellationToken = default) =>
        db.ShoppingCarts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CartSessionKey == cartSessionKey, cancellationToken);

    public void Remove(ShoppingCart cart) => db.ShoppingCarts.Remove(cart);

    public async Task AddAsync(ShoppingCart cart, CancellationToken cancellationToken = default) =>
        await db.ShoppingCarts.AddAsync(cart, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfOrderRepository(OrdersDbContext db) : IOrderRepository
{
    private IQueryable<Order> FullQuery() =>
        db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory);

    public Task<Order?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.Orders.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> ListKanbanActiveAsync(CancellationToken cancellationToken = default)
    {
        var list = await FullQuery()
            .AsNoTracking()
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        return list.OrderBy(o => o.PlacedAtUtc).ToList();
    }

    public Task<bool> HasProductReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        db.OrderItems.AnyAsync(i => i.TenantId == tenantId && i.ProductId == productId, cancellationToken);

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default) =>
        await db.Orders.AddAsync(order, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Implementação real de verificação histórica de produtos baseada em pedidos persistidos (Seção 12).
/// </summary>
public sealed class OrderProductHistoryReferenceChecker(IOrderRepository orderRepo) : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        orderRepo.HasProductReferencesAsync(tenantId, productId, cancellationToken);
}
