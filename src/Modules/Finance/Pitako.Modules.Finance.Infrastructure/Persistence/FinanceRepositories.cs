using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Domain;

namespace Pitako.Modules.Finance.Infrastructure.Persistence;

public sealed class EfFinancialEntryRepository(FinanceDbContext db) : IFinancialEntryRepository
{
    public Task<FinancialEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.FinancialEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.FinancialEntries.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<FinancialEntry>> ListAllInPeriodAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.FinancialEntries
                .AsNoTracking()
                .Where(e => e.DueDateUtc >= startUtc && e.DueDateUtc <= endUtc)
                .ToListAsync(cancellationToken);
        }

        var list = await db.FinancialEntries.AsNoTracking().ToListAsync(cancellationToken);
        return list.Where(e => e.DueDateUtc >= startUtc && e.DueDateUtc <= endUtc).ToList();
    }

    public async Task<IReadOnlyList<FinancialEntry>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await db.FinancialEntries
            .AsNoTracking()
            .Where(e => e.Status == FinancialEntryStatus.Open || e.Status == FinancialEntryStatus.PartiallySettled)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(FinancialEntry entry, CancellationToken cancellationToken = default) =>
        await db.FinancialEntries.AddAsync(entry, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfCashFlowLedgerRepository(FinanceDbContext db) : ICashFlowLedgerRepository
{
    public async Task<IReadOnlyList<CashFlowLedgerEntry>> ListInPeriodAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.CashFlowLedger
                .AsNoTracking()
                .Where(l => l.OccurredAtUtc >= startUtc && l.OccurredAtUtc <= endUtc)
                .OrderBy(l => l.OccurredAtUtc)
                .ToListAsync(cancellationToken);
        }

        var list = await db.CashFlowLedger.AsNoTracking().ToListAsync(cancellationToken);
        return list.Where(l => l.OccurredAtUtc >= startUtc && l.OccurredAtUtc <= endUtc).OrderBy(l => l.OccurredAtUtc).ToList();
    }

    public async Task AddAsync(CashFlowLedgerEntry entry, CancellationToken cancellationToken = default) =>
        await db.CashFlowLedger.AddAsync(entry, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPaymentGatewayConfigRepository(FinanceDbContext db) : IPaymentGatewayConfigRepository
{
    public Task<PaymentGatewayConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PaymentGatewayConfigs.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task AddAsync(PaymentGatewayConfig config, CancellationToken cancellationToken = default) =>
        await db.PaymentGatewayConfigs.AddAsync(config, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPaymentTransactionRepository(FinanceDbContext db) : IPaymentTransactionRepository
{
    public async Task<IReadOnlyList<PaymentTransactionRecord>> FindByExternalRefsAsync(
        IEnumerable<string> externalRefs,
        CancellationToken cancellationToken = default)
    {
        var refs = externalRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return await db.PaymentTransactions
            .AsNoTracking()
            .Where(t => refs.Contains(t.ExternalTransactionId))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PaymentTransactionRecord record, CancellationToken cancellationToken = default) =>
        await db.PaymentTransactions.AddAsync(record, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfReconciliationBatchRepository(FinanceDbContext db) : IReconciliationBatchRepository
{
    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.ReconciliationBatches.CountAsync(cancellationToken);

    public async Task AddAsync(SettlementReconciliationBatch batch, CancellationToken cancellationToken = default) =>
        await db.ReconciliationBatches.AddAsync(batch, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
