using Pitako.Modules.Orders.Domain;

namespace Pitako.Modules.Orders.Application.Abstractions;

public interface IShoppingCartRepository
{
    Task<ShoppingCart?> FindBySessionKeyAsync(string cartSessionKey, CancellationToken cancellationToken = default);
    Task AddAsync(ShoppingCart cart, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
