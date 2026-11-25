using Microsoft.EntityFrameworkCore;
using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Domain;

namespace Pitako.Modules.PdvCashier.Infrastructure.Persistence;

public sealed class EfCashRegisterRepository(PdvCashierDbContext db) : ICashRegisterRepository
{
    private IQueryable<CashRegisterSession> FullQuery() =>
        db.CashRegisterSessions.Include(s => s.Movements);

    public Task<CashRegisterSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public Task<CashRegisterSession?> FindOpenByTerminalAsync(string normalizedTerminalCode, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(s => s.TerminalCode == normalizedTerminalCode && s.Status == CashRegisterStatus.Open, cancellationToken);

    public async Task AddAsync(CashRegisterSession session, CancellationToken cancellationToken = default) =>
        await db.CashRegisterSessions.AddAsync(session, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPdvSaleRepository(PdvCashierDbContext db) : IPdvSaleRepository
{
    private IQueryable<PdvSale> FullQuery() =>
        db.PdvSales.Include(s => s.Payments);

    public Task<PdvSale?> FindByIdAsync(Guid saleId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);

    public Task<PdvSale?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.PdvSales.CountAsync(cancellationToken);

    public async Task AddAsync(PdvSale sale, CancellationToken cancellationToken = default) =>
        await db.PdvSales.AddAsync(sale, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
