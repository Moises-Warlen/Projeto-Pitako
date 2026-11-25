using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Domain;

namespace Pitako.Modules.Reports.Infrastructure.Persistence;

public sealed class EfReportExportAuditRepository(ReportsDbContext db) : IReportExportAuditRepository
{
    public async Task<IReadOnlyList<ReportExportAuditLog>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.ExportAuditLogs
                .AsNoTracking()
                .OrderByDescending(l => l.ExportedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        var list = await db.ExportAuditLogs.AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(l => l.ExportedAtUtc).Take(limit).ToList();
    }

    public async Task AddAsync(ReportExportAuditLog auditLog, CancellationToken cancellationToken = default) =>
        await db.ExportAuditLogs.AddAsync(auditLog, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
