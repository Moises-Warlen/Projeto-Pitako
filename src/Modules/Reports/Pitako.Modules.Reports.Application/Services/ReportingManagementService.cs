using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Application.Export;
using Pitako.Modules.Reports.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Reports.Application.Services;

/// <summary>
/// Serviço gerencial de geração, consulta e exportação auditada de relatórios (PDF, Excel XLSX e CSV)
/// isolados estritamente por estabelecimento e permissões RBAC (Seção 30).
/// </summary>
public sealed class ReportingManagementService
{
    private readonly IReportDataAggregator _dataAggregator;
    private readonly IReportExportAuditRepository _auditRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public ReportingManagementService(
        IReportDataAggregator dataAggregator,
        IReportExportAuditRepository auditRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _dataAggregator = dataAggregator;
        _auditRepo = auditRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<ReportDataset>> GenerateOperationalReportAsync(
        User actor,
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<ReportDataset>(guard.Error);

        var dsRes = await _dataAggregator.BuildDatasetAsync(guard.Value, filter, cancellationToken);
        if (dsRes.IsFailure)
            return dsRes;

        if (dsRes.Value.TenantId != guard.Value)
            throw new CrossTenantAccessException(dsRes.Value.TenantId, guard.Value, "Dataset retornado pertence a outro estabelecimento.");

        return dsRes;
    }

    public async Task<Result<ExportedReportFile>> ExportReportAsync(
        User actor,
        ReportQueryFilter filter,
        ReportExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var guard = ValidateActor(actor, PermissionAction.View | PermissionAction.Export);
        if (guard.IsFailure)
            return Result.Failure<ExportedReportFile>(guard.Error);

        var dsRes = await _dataAggregator.BuildDatasetAsync(guard.Value, filter, cancellationToken);
        if (dsRes.IsFailure)
            return Result.Failure<ExportedReportFile>(dsRes.Error);

        if (dsRes.Value.TenantId != guard.Value)
            throw new CrossTenantAccessException(dsRes.Value.TenantId, guard.Value, "Exportação bloqueada por divergência de tenant.");

        var file = ReportFormatExporters.Export(dsRes.Value, format);
        var auditRes = ReportExportAuditLog.Record(
            guard.Value,
            filter.Category,
            format,
            file.FileName,
            file.MimeType,
            file.RowCount,
            file.ContentBytes.Length,
            file.Sha256Hex,
            filter.PeriodStartUtc,
            filter.PeriodEndUtc,
            actor.Id,
            _clock.UtcNow);

        if (auditRes.IsFailure)
            return Result.Failure<ExportedReportFile>(auditRes.Error);

        await _auditRepo.AddAsync(auditRes.Value, cancellationToken);
        await _auditRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }

    public async Task<Result<IReadOnlyList<ReportExportAuditLog>>> ListExportHistoryAsync(
        User actor,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<IReadOnlyList<ReportExportAuditLog>>(guard.Error);

        var list = await _auditRepo.ListRecentAsync(limit, cancellationToken);
        return Result.Success(list);
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction requiredActions)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de acessar relatórios de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Reports, requiredActions))
            return Result.Failure<Guid>(Error.Forbidden("Reports.Forbidden", "Permissão insuficiente para visualizar ou exportar relatórios."));

        return Result.Success(tenantId);
    }
}
