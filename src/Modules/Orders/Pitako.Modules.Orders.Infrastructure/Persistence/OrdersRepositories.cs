using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;

namespace Pitako.Modules.Orders.Infrastructure.Persistence;

public sealed class EfShoppingCartRepository(OrdersDbContext db) : IShoppingCartRepository
{
    public Task<ShoppingCart?> FindBySessionKeyAsync(string cartSessionKey, CancellationToken cancellationToken = default) =>
        db.ShoppingCarts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CartSessionKey == cartSessionKey, cancellationToken);

    public async Task AddAsync(ShoppingCart cart, CancellationToken cancellationToken = default) =>
        await db.ShoppingCarts.AddAsync(cart, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
