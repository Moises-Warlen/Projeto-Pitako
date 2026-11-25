using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Reports.Domain;

public enum ReportCategory
{
    SalesByPeriod = 1,
    SalesByChannel = 2,
    TopSellingItems = 3,
    CashierClosings = 4,
    CourierRunsAndCommissions = 5,
    MarketplaceConsolidation = 6,
    InventoryValuationAndLowStock = 7,
    StoreOperationalSummary = 8
}

public enum ReportExportFormat
{
    Csv = 1,
    ExcelXlsx = 2,
    Pdf = 3
}

public sealed record ReportQueryFilter(
    ReportCategory Category,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    Guid? StoreUnitId = null,
    string? ChannelFilter = null);

/// <summary>
/// Conjunto tabular consolidado de métricas operacionais/gerenciais restrito a um único estabelecimento.
/// </summary>
public sealed class ReportDataset
{
    private ReportDataset(
        Guid tenantId,
        ReportCategory category,
        string reportTitle,
        string storeTradeName,
        string storeFormattedCnpj,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, decimal> kpis)
    {
        TenantId = tenantId;
        Category = category;
        ReportTitle = reportTitle;
        StoreTradeName = storeTradeName;
        StoreFormattedCnpj = storeFormattedCnpj;
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        GeneratedAtUtc = generatedAtUtc;
        Columns = columns;
        Rows = rows;
        Kpis = kpis;
    }

    public Guid TenantId { get; }
    public ReportCategory Category { get; }
    public string ReportTitle { get; }
    public string StoreTradeName { get; }
    public string StoreFormattedCnpj { get; }
    public DateTimeOffset PeriodStartUtc { get; }
    public DateTimeOffset PeriodEndUtc { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public IReadOnlyDictionary<string, decimal> Kpis { get; }

    public static Result<ReportDataset> Create(
        Guid tenantId,
        ReportCategory category,
        string reportTitle,
        string storeTradeName,
        string rawStoreCnpj,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, decimal>? kpis = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ReportDataset>(Error.Validation("Report.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(reportTitle))
            return Result.Failure<ReportDataset>(Error.Validation("Report.TitleRequired", "Título do relatório é obrigatório."));
        if (string.IsNullOrWhiteSpace(storeTradeName))
            return Result.Failure<ReportDataset>(Error.Validation("Report.StoreNameRequired", "Nome do estabelecimento é obrigatório."));
        if (periodEndUtc < periodStartUtc)
            return Result.Failure<ReportDataset>(Error.Validation("Report.InvalidPeriod", "Data final do relatório deve ser igual ou posterior à inicial."));
        if (columns is null || columns.Count == 0)
            return Result.Failure<ReportDataset>(Error.Validation("Report.ColumnsRequired", "Relatório deve possuir ao menos uma coluna."));

        var normalizedBrand = storeTradeName.Trim().ToUpperInvariant();
        if (normalizedBrand is "PITAKO" or "PITAKO SAAS")
            return Result.Failure<ReportDataset>(Error.Validation("Report.PitakoBrandingForbidden", "Relatórios operacionais da loja devem exibir exclusivamente a marca e CNPJ do estabelecimento cliente."));

        var cnpjRes = Cnpj.Create(rawStoreCnpj);
        if (cnpjRes.IsFailure)
            return Result.Failure<ReportDataset>(cnpjRes.Error);

        return Result.Success(new ReportDataset(
            tenantId,
            category,
            reportTitle.Trim(),
            storeTradeName.Trim(),
            cnpjRes.Value.Formatted,
            periodStartUtc,
            periodEndUtc,
            generatedAtUtc,
            columns.ToList(),
            (rows ?? []).Select(r => (IReadOnlyList<string>)r.ToList()).ToList(),
            kpis is null ? new Dictionary<string, decimal>() : new Dictionary<string, decimal>(kpis)));
    }
}

/// <summary>
/// Registro auditável imutável de exportações PDF, Excel (.xlsx) e CSV emitidas por usuários do estabelecimento (Seção 30).
/// </summary>
public sealed class ReportExportAuditLog : TenantScopedAggregateRoot
{
    private ReportExportAuditLog()
    {
        FileName = string.Empty;
        MimeType = string.Empty;
        ContentSha256 = string.Empty;
    }

    private ReportExportAuditLog(
        Guid id,
        Guid tenantId,
        ReportCategory category,
        ReportExportFormat format,
        string fileName,
        string mimeType,
        int rowCount,
        long fileSizeBytes,
        string contentSha256,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        Guid exportedByUserId,
        DateTimeOffset exportedAtUtc) : base(id, tenantId)
    {
        Category = category;
        Format = format;
        FileName = fileName;
        MimeType = mimeType;
        RowCount = rowCount;
        FileSizeBytes = fileSizeBytes;
        ContentSha256 = contentSha256;
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        ExportedByUserId = exportedByUserId;
        ExportedAtUtc = exportedAtUtc;
    }

    public ReportCategory Category { get; private set; }
    public ReportExportFormat Format { get; private set; }
    public string FileName { get; private set; }
    public string MimeType { get; private set; }
    public int RowCount { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string ContentSha256 { get; private set; }
    public DateTimeOffset PeriodStartUtc { get; private set; }
    public DateTimeOffset PeriodEndUtc { get; private set; }
    public Guid ExportedByUserId { get; private set; }
    public DateTimeOffset ExportedAtUtc { get; private set; }

    public static Result<ReportExportAuditLog> Record(
        Guid tenantId,
        ReportCategory category,
        ReportExportFormat format,
        string fileName,
        string mimeType,
        int rowCount,
        long fileSizeBytes,
        string contentSha256,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        Guid exportedByUserId,
        DateTimeOffset exportedAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ReportExportAuditLog>(Error.Validation("ReportAudit.TenantRequired", "TenantId é obrigatório."));
        if (exportedByUserId == Guid.Empty)
            return Result.Failure<ReportExportAuditLog>(Error.Validation("ReportAudit.UserRequired", "Usuário exportador é obrigatório."));
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentSha256))
            return Result.Failure<ReportExportAuditLog>(Error.Validation("ReportAudit.FileMetaRequired", "Metadados do arquivo exportado são obrigatórios."));

        return Result.Success(new ReportExportAuditLog(
            Guid.NewGuid(),
            tenantId,
            category,
            format,
            fileName.Trim(),
            mimeType.Trim(),
            Math.Max(0, rowCount),
            Math.Max(0, fileSizeBytes),
            contentSha256.Trim(),
            periodStartUtc,
            periodEndUtc,
            exportedByUserId,
            exportedAtUtc));
    }
}
