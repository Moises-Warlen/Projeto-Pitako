using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Application.Services;
using Pitako.Modules.Reports.Domain;
using Pitako.Modules.Reports.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class TestReportAggregator(Guid expectedTenantId, string tradeName) : IReportDataAggregator
{
    public Task<Result<ReportDataset>> BuildDatasetAsync(Guid tenantId, ReportQueryFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportDataset.Create(
            expectedTenantId,
            filter.Category,
            $"Relatório {filter.Category}",
            tradeName,
            "11222333000181",
            filter.PeriodStartUtc,
            filter.PeriodEndUtc,
            filter.PeriodEndUtc,
            ["Canal", "Pedidos", "Total"],
            [
                ["PDV", "15", "750.00"],
                ["Delivery Próprio", "10", "520.00"]
            ],
            new Dictionary<string, decimal> { ["Total"] = 1270.00m }));
}

public sealed class ReportsWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 58, 0, TimeSpan.Zero));

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
    public async Task ReportingService_ShouldGenerateAndExport_PdfXlsxCsv_WithPersistentAuditLog()
    {
        var tenantId = Guid.NewGuid();
        var financial = User.CreateEstablishmentUser(tenantId, "Financeiro Bia", "fin@loja.com", "hash", EstablishmentRole.Financial).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var filter = new ReportQueryFilter(
            ReportCategory.SalesByChannel,
            _clock.UtcNow.AddDays(-7),
            _clock.UtcNow);

        await using var db = CreateDb(ctx);
        var svc = new ReportingManagementService(
            new TestReportAggregator(tenantId, "Açaí do Vale"),
            new EfReportExportAuditRepository(db),
            ctx,
            _clock);

        var ds = (await svc.GenerateOperationalReportAsync(financial, filter)).Value;
        Assert.Equal(2, ds.Rows.Count);
        Assert.Equal("Açaí do Vale", ds.StoreTradeName);

        var csv = (await svc.ExportReportAsync(financial, filter, ReportExportFormat.Csv)).Value;
        var xlsx = (await svc.ExportReportAsync(financial, filter, ReportExportFormat.ExcelXlsx)).Value;
        var pdf = (await svc.ExportReportAsync(financial, filter, ReportExportFormat.Pdf)).Value;

        Assert.True(csv.ContentBytes.Length > 50);
        using (var zip = new ZipArchive(new MemoryStream(xlsx.ContentBytes), ZipArchiveMode.Read))
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.StartsWith("%PDF-1.4", Encoding.Latin1.GetString(pdf.ContentBytes), StringComparison.Ordinal);

        var history = (await svc.ListExportHistoryAsync(financial)).Value;
        Assert.Equal(3, history.Count);
        Assert.Contains(history, h => h.Format == ReportExportFormat.Pdf && h.ContentSha256 == pdf.Sha256Hex);
        Assert.Contains(history, h => h.Format == ReportExportFormat.ExcelXlsx && h.ContentSha256 == xlsx.Sha256Hex);
        Assert.Contains(history, h => h.Format == ReportExportFormat.Csv && h.ContentSha256 == csv.Sha256Hex);
    }
}
