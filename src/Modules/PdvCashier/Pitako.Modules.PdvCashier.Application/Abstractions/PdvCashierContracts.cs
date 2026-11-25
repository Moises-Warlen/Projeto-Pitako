using Pitako.Modules.PdvCashier.Domain;

namespace Pitako.Modules.PdvCashier.Application.Abstractions;

public interface ICashRegisterRepository
{
    Task<CashRegisterSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<CashRegisterSession?> FindOpenByTerminalAsync(string normalizedTerminalCode, CancellationToken cancellationToken = default);
    Task AddAsync(CashRegisterSession session, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPdvSaleRepository
{
    Task<PdvSale?> FindByIdAsync(Guid saleId, CancellationToken cancellationToken = default);
    Task<PdvSale?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PdvSale sale, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
