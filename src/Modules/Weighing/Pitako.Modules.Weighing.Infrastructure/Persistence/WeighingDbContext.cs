using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Weighing.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Weighing.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Pesagem e Balança com isolamento multi-tenant.
/// </summary>
public sealed class WeighingDbContext : PitakoDbContextBase
{
    public const string SchemaName = "weighing";

    public WeighingDbContext(
        DbContextOptions<WeighingDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<ScaleDeviceConfig> ScaleDevices => Set<ScaleDeviceConfig>();
    public DbSet<TarePreset> TarePresets => Set<TarePreset>();
    public DbSet<WeighingRecord> WeighingRecords => Set<WeighingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new ScaleDeviceConfigConfiguration());
        modelBuilder.ApplyConfiguration(new TarePresetConfiguration());
        modelBuilder.ApplyConfiguration(new WeighingRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ScaleDeviceConfigConfiguration : IEntityTypeConfiguration<ScaleDeviceConfig>
{
    public void Configure(EntityTypeBuilder<ScaleDeviceConfig> builder)
    {
        builder.ToTable("scale_devices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Manufacturer).HasColumnName("manufacturer").IsRequired();
        builder.Property(x => x.ConnectionType).HasColumnName("connection_type").IsRequired();
        builder.Property(x => x.ProtocolProfile).HasColumnName("protocol_profile").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EndpointOrPort).HasColumnName("endpoint_or_port").HasMaxLength(160).IsRequired();
        builder.Property(x => x.BaudRate).HasColumnName("baud_rate").IsRequired();
        builder.Property(x => x.AllowManualFallback).HasColumnName("allow_manual_fallback").IsRequired();
        builder.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

internal sealed class TarePresetConfiguration : IEntityTypeConfiguration<TarePreset>
{
    public void Configure(EntityTypeBuilder<TarePreset> builder)
    {
        builder.ToTable("tare_presets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.TareWeightGrams).HasColumnName("tare_weight_grams").HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

internal sealed class WeighingRecordConfiguration : IEntityTypeConfiguration<WeighingRecord>
{
    public void Configure(EntityTypeBuilder<WeighingRecord> builder)
    {
        builder.ToTable("weighing_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ScaleDeviceId).HasColumnName("scale_device_id");
        builder.Property(x => x.TarePresetId).HasColumnName("tare_preset_id");
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(180).IsRequired();
        builder.Property(x => x.CaptureMode).HasColumnName("capture_mode").IsRequired();
        builder.Property(x => x.GrossWeightGrams).HasColumnName("gross_weight_grams").HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.TareWeightGrams).HasColumnName("tare_weight_grams").HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.NetWeightGrams).HasColumnName("net_weight_grams").HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.OperatorUserId).HasColumnName("operator_user_id").IsRequired();
        builder.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
        builder.Property(x => x.ManualFallbackReason).HasColumnName("manual_fallback_reason").HasMaxLength(300);
        builder.Property(x => x.RawScalePayload).HasColumnName("raw_scale_payload").HasMaxLength(200);
        builder.Property(x => x.LinkedOrderId).HasColumnName("linked_order_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.PricePerKilogram, m =>
        {
            m.Property(p => p.Amount).HasColumnName("price_per_kg_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("price_per_kg_currency").HasMaxLength(3).IsRequired();
        });
        builder.OwnsOne(x => x.TotalPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("total_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("total_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.CapturedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.LinkedOrderId });
    }
}
