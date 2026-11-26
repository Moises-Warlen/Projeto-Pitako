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

    public Task<Order?> FindByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        var raw = orderNumber.Trim().ToUpperInvariant();
        var withHash = raw.StartsWith('#') ? raw : "#" + raw;
        return FullQuery().AsNoTracking().FirstOrDefaultAsync(
            o => o.OrderNumber == raw || o.OrderNumber == withHash,
            cancellationToken);
    }

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

    public async Task<IReadOnlyList<Order>> ListRecentAllAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        var list = await FullQuery().AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(o => o.PlacedAtUtc).Take(Math.Clamp(limit, 1, 500)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetProductSalesCountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.OrderItems
            .AsNoTracking()
            .Where(i => i.ProductId.HasValue)
            .Select(i => new { ProductId = i.ProductId!.Value, i.Quantity })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
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
