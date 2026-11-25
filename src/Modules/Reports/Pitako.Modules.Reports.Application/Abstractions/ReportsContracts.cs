using Pitako.Modules.Reports.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Reports.Application.Abstractions;

public sealed record ExportedReportFile(
    string FileName,
    string MimeType,
    byte[] ContentBytes,
    string Sha256Hex,
    int RowCount);

public interface IReportExportAuditRepository
{
    Task<IReadOnlyList<ReportExportAuditLog>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task AddAsync(ReportExportAuditLog auditLog, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Agregador multi-módulo isolado por tenant para consolidação de relatórios gerenciais reais (Seção 30).
/// </summary>
public interface IReportDataAggregator
{
    Task<Result<ReportDataset>> BuildDatasetAsync(
        Guid tenantId,
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default);
}
