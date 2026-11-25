using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Reports.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Reports.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Relatórios e auditoria de exportações com isolamento multi-tenant.
/// </summary>
public sealed class ReportsDbContext : PitakoDbContextBase
{
    public const string SchemaName = "reports";

    public ReportsDbContext(
        DbContextOptions<ReportsDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<ReportExportAuditLog> ExportAuditLogs => Set<ReportExportAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new ReportExportAuditLogConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ReportExportAuditLogConfiguration : IEntityTypeConfiguration<ReportExportAuditLog>
{
    public void Configure(EntityTypeBuilder<ReportExportAuditLog> builder)
    {
        builder.ToTable("report_export_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").IsRequired();
        builder.Property(x => x.Format).HasColumnName("format").IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(120).IsRequired();
        builder.Property(x => x.RowCount).HasColumnName("row_count").IsRequired();
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        builder.Property(x => x.ContentSha256).HasColumnName("content_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
        builder.Property(x => x.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
        builder.Property(x => x.ExportedByUserId).HasColumnName("exported_by_user_id").IsRequired();
        builder.Property(x => x.ExportedAtUtc).HasColumnName("exported_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Category, x.ExportedAtUtc });
    }
}
