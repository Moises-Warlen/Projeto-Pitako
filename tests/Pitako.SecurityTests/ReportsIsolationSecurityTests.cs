using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Application.Services;
using Pitako.Modules.Reports.Domain;
using Pitako.Modules.Reports.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecuritySpoofCheckAggregator(Guid returnedTenantId) : IReportDataAggregator
{
    public Task<Result<ReportDataset>> BuildDatasetAsync(Guid tenantId, ReportQueryFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportDataset.Create(
            returnedTenantId,
            filter.Category,
            "Relatório",
            "Loja",
            "11222333000181",
            filter.PeriodStartUtc,
            filter.PeriodEndUtc,
            filter.PeriodEndUtc,
            ["Col"],
            [["Val"]]));
}

public sealed class ReportsIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ReportsDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<ReportsDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverSee_TenantB_ReportAudits_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var financialA = User.CreateEstablishmentUser(tenantA, "Fin A", "fin@a.com", "hash", EstablishmentRole.Financial).Value;
        var waiterA = User.CreateEstablishmentUser(tenantA, "Garçom A", "gar@a.com", "hash", EstablishmentRole.Waiter).Value;
        var cashierA = User.CreateEstablishmentUser(tenantA, "Caixa A", "cx@a.com", "hash", EstablishmentRole.Cashier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var filter = new ReportQueryFilter(ReportCategory.CashierClosings, _clock.UtcNow.AddDays(-1), _clock.UtcNow);

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new ReportingManagementService(new SecuritySpoofCheckAggregator(tenantB), new EfReportExportAuditRepository(dbB), ctxB, _clock);
            await svcB.ExportReportAsync(adminB, filter, ReportExportFormat.Pdf);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new ReportingManagementService(new SecuritySpoofCheckAggregator(tenantA), new EfReportExportAuditRepository(dbA), ctxA, _clock);

        // Global Query Filter oculta exportações da Loja B
        Assert.Empty(await dbA.ExportAuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty((await svcA.ListExportHistoryAsync(financialA)).Value);

        // Guard cross-tenant se um agregador tentar devolver dataset de outro tenant
        var spoofSvc = new ReportingManagementService(new SecuritySpoofCheckAggregator(tenantB), new EfReportExportAuditRepository(dbA), ctxA, _clock);
        await Assert.ThrowsAsync<CrossTenantAccessException>(() => spoofSvc.ExportReportAsync(financialA, filter, ReportExportFormat.Csv));

        // RBAC menor privilégio: Garçom e Caixa não podem visualizar nem exportar relatórios gerenciais
        var waiterRes = await svcA.ExportReportAsync(waiterA, filter, ReportExportFormat.Csv);
        Assert.True(waiterRes.IsFailure);
        Assert.Equal("Reports.Forbidden", waiterRes.Error.Code);

        var cashierRes = await svcA.GenerateOperationalReportAsync(cashierA, filter);
        Assert.True(cashierRes.IsFailure);
        Assert.Equal("Reports.Forbidden", cashierRes.Error.Code);
    }
}
