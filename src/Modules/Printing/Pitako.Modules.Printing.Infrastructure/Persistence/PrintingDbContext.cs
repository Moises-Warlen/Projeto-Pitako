using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Printing.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Printing.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Impressoras, Setores de Produção e Fila ESC/POS com isolamento multi-tenant.
/// </summary>
public sealed class PrintingDbContext : PitakoDbContextBase
{
    public const string SchemaName = "printing";

    public PrintingDbContext(
        DbContextOptions<PrintingDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<PrinterProfile> Printers => Set<PrinterProfile>();
    public DbSet<SectorRoutingRule> SectorRoutingRules => Set<SectorRoutingRule>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new PrinterProfileConfiguration());
        modelBuilder.ApplyConfiguration(new SectorRoutingRuleConfiguration());
        modelBuilder.ApplyConfiguration(new PrintJobConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class PrinterProfileConfiguration : IEntityTypeConfiguration<PrinterProfile>
{
    public void Configure(EntityTypeBuilder<PrinterProfile> builder)
    {
        builder.ToTable("printer_profiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.PrinterCode).HasColumnName("printer_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ConnectionKind).HasColumnName("connection_kind").IsRequired();
        builder.Property(x => x.EndpointOrPort).HasColumnName("endpoint_or_port").HasMaxLength(160).IsRequired();
        builder.Property(x => x.PaperWidthMm).HasColumnName("paper_width_mm").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.BackupPrinterId).HasColumnName("backup_printer_id");
        builder.Property(x => x.SupportsLogoRaster).HasColumnName("supports_logo_raster").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.PrinterCode }).IsUnique();
    }
}

internal sealed class SectorRoutingRuleConfiguration : IEntityTypeConfiguration<SectorRoutingRule>
{
    public void Configure(EntityTypeBuilder<SectorRoutingRule> builder)
    {
        builder.ToTable("sector_routing_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.SectorType).HasColumnName("sector_type").IsRequired();
        builder.Property(x => x.CategoryId).HasColumnName("category_id");
        builder.Property(x => x.PrimaryPrinterId).HasColumnName("primary_printer_id").IsRequired();
        builder.Property(x => x.FallbackPrinterId).HasColumnName("fallback_printer_id");
        builder.Property(x => x.CopiesCount).HasColumnName("copies_count").IsRequired();
        builder.Property(x => x.AutoPrintOnOrderAccepted).HasColumnName("auto_print_on_order_accepted").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.SectorType, x.CategoryId }).IsUnique();
    }
}

internal sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("print_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SectorType).HasColumnName("sector_type").IsRequired();
        builder.Property(x => x.DocumentKind).HasColumnName("document_kind").IsRequired();
        builder.Property(x => x.OrderOrTabReference).HasColumnName("order_or_tab_reference").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetPrinterId).HasColumnName("target_printer_id").IsRequired();
        builder.Property(x => x.ActualPrinterId).HasColumnName("actual_printer_id");
        builder.Property(x => x.UsedFallback).HasColumnName("used_fallback").IsRequired();
        builder.Property(x => x.FallbackReason).HasColumnName("fallback_reason").HasMaxLength(300);
        builder.Property(x => x.StoreTradeName).HasColumnName("store_trade_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.StoreCnpj).HasColumnName("store_cnpj").HasMaxLength(24).IsRequired();
        builder.Property(x => x.StoreLogoRef).HasColumnName("store_logo_ref").HasMaxLength(300).IsRequired();
        builder.Property(x => x.RenderedPlainText).HasColumnName("rendered_plain_text").HasMaxLength(12000).IsRequired();
        builder.Property(x => x.EscPosPayloadBase64).HasColumnName("esc_pos_payload_base64").HasMaxLength(20000).IsRequired();
        builder.Property(x => x.CopiesCount).HasColumnName("copies_count").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
        builder.Property(x => x.JobCreatedAtUtc).HasColumnName("job_created_at_utc").IsRequired();
        builder.Property(x => x.PrintedAtUtc).HasColumnName("printed_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.JobCreatedAtUtc });
    }
}
