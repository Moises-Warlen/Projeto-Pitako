using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;

namespace Pitako.Modules.Printing.Infrastructure.Persistence;

public sealed class EfPrinterProfileRepository(PrintingDbContext db) : IPrinterProfileRepository
{
    public Task<PrinterProfile?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Printers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PrinterProfile?> FindByCodeAsync(string printerCode, CancellationToken cancellationToken = default) =>
        db.Printers.FirstOrDefaultAsync(p => p.PrinterCode == printerCode, cancellationToken);

    public async Task<IReadOnlyList<PrinterProfile>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Printers.AsNoTracking().OrderBy(p => p.PrinterCode).ToListAsync(cancellationToken);

    public async Task AddAsync(PrinterProfile printer, CancellationToken cancellationToken = default) =>
        await db.Printers.AddAsync(printer, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfSectorRoutingRuleRepository(PrintingDbContext db) : ISectorRoutingRuleRepository
{
    public async Task<SectorRoutingRule?> FindMatchingRuleAsync(
        ProductionSectorType sectorType,
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await db.SectorRoutingRules
            .Where(r => r.IsActive && r.SectorType == sectorType && (r.CategoryId == categoryId || r.CategoryId == null))
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(r => categoryId.HasValue && r.CategoryId == categoryId)
               ?? candidates.FirstOrDefault(r => r.CategoryId == null);
    }

    public async Task<IReadOnlyList<SectorRoutingRule>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.SectorRoutingRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);

    public async Task AddAsync(SectorRoutingRule rule, CancellationToken cancellationToken = default) =>
        await db.SectorRoutingRules.AddAsync(rule, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfPrintJobRepository(PrintingDbContext db) : IPrintJobRepository
{
    public Task<PrintJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PrintJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.PrintJobs
                .AsNoTracking()
                .OrderByDescending(j => j.JobCreatedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        var all = await db.PrintJobs.AsNoTracking().ToListAsync(cancellationToken);
        return all.OrderByDescending(j => j.JobCreatedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(PrintJob job, CancellationToken cancellationToken = default) =>
        await db.PrintJobs.AddAsync(job, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
