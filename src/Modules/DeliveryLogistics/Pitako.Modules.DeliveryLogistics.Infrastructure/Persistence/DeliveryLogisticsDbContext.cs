using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Delivery Próprio e Logística com isolamento multi-tenant.
/// </summary>
public sealed class DeliveryLogisticsDbContext : PitakoDbContextBase
{
    public const string SchemaName = "delivery";

    public DeliveryLogisticsDbContext(
        DbContextOptions<DeliveryLogisticsDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<DeliveryPolicy> DeliveryPolicies => Set<DeliveryPolicy>();
    public DbSet<DeliveryDistanceTier> DistanceTiers => Set<DeliveryDistanceTier>();
    public DbSet<DeliveryZoneRule> ZoneRules => Set<DeliveryZoneRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new DeliveryPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryDistanceTierConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryZoneRuleConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class DeliveryMoneyMapping
{
    public static void ConfigureRequired<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(p => p.Amount).HasColumnName(amountCol).HasPrecision(18, 2).IsRequired();
        money.Property(p => p.Currency).HasColumnName(currencyCol).HasMaxLength(3).IsRequired();
    }

    public static void ConfigureOptional<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(p => p.Amount).HasColumnName(amountCol).HasPrecision(18, 2);
        money.Property(p => p.Currency).HasColumnName(currencyCol).HasMaxLength(3);
    }
}

internal sealed class DeliveryPolicyConfiguration : IEntityTypeConfiguration<DeliveryPolicy>
{
    public void Configure(EntityTypeBuilder<DeliveryPolicy> builder)
    {
        builder.ToTable("delivery_policies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.OriginLatitude).HasColumnName("origin_latitude").IsRequired();
        builder.Property(x => x.OriginLongitude).HasColumnName("origin_longitude").IsRequired();
        builder.Property(x => x.MaxRadiusKm).HasColumnName("max_radius_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.CalculationMode).HasColumnName("calculation_mode").IsRequired();
        builder.Property(x => x.IncludedKm).HasColumnName("included_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.BaseEstimatedMinutes).HasColumnName("base_estimated_minutes").IsRequired();
        builder.Property(x => x.IsDeliveryEnabled).HasColumnName("is_delivery_enabled").IsRequired();
        builder.Property(x => x.AllowPickup).HasColumnName("allow_pickup").IsRequired();
        builder.Property(x => x.IsTemporarilyPaused).HasColumnName("is_temporarily_paused").IsRequired();
        builder.Property(x => x.PauseReason).HasColumnName("pause_reason").HasMaxLength(240);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.BaseFee, m => DeliveryMoneyMapping.ConfigureRequired(m, "base_fee_amount", "base_fee_currency"));
        builder.OwnsOne(x => x.FeePerKm, m => DeliveryMoneyMapping.ConfigureRequired(m, "fee_per_km_amount", "fee_per_km_currency"));
        builder.OwnsOne(x => x.MinimumOrderAmount, m => DeliveryMoneyMapping.ConfigureRequired(m, "minimum_order_amount", "minimum_order_currency"));
        builder.OwnsOne(x => x.FreeShippingThreshold, m => DeliveryMoneyMapping.ConfigureOptional(m, "free_shipping_threshold_amount", "free_shipping_threshold_currency"));

        builder.HasMany(x => x.DistanceTiers).WithOne().HasForeignKey(t => t.DeliveryPolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.DistanceTiers).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.ZoneRules).WithOne().HasForeignKey(z => z.DeliveryPolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.ZoneRules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.StoreUnitId });
    }
}

internal sealed class DeliveryDistanceTierConfiguration : IEntityTypeConfiguration<DeliveryDistanceTier>
{
    public void Configure(EntityTypeBuilder<DeliveryDistanceTier> builder)
    {
        builder.ToTable("delivery_distance_tiers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DeliveryPolicyId).HasColumnName("delivery_policy_id").IsRequired();
        builder.Property(x => x.MinKm).HasColumnName("min_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.MaxKm).HasColumnName("max_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.EstimatedMinutes).HasColumnName("estimated_minutes").IsRequired();
        builder.OwnsOne(x => x.Fee, m => DeliveryMoneyMapping.ConfigureRequired(m, "fee_amount", "fee_currency"));
        builder.HasIndex(x => new { x.TenantId, x.DeliveryPolicyId, x.MinKm });
    }
}

internal sealed class DeliveryZoneRuleConfiguration : IEntityTypeConfiguration<DeliveryZoneRule>
{
    public void Configure(EntityTypeBuilder<DeliveryZoneRule> builder)
    {
        builder.ToTable("delivery_zone_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DeliveryPolicyId).HasColumnName("delivery_policy_id").IsRequired();
        builder.Property(x => x.ZoneName).HasColumnName("zone_name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.ZipCodePrefix).HasColumnName("zip_code_prefix").HasMaxLength(16);
        builder.Property(x => x.EstimatedMinutes).HasColumnName("estimated_minutes").IsRequired();
        builder.Property(x => x.IsBlocked).HasColumnName("is_blocked").IsRequired();
        builder.OwnsOne(x => x.Fee, m => DeliveryMoneyMapping.ConfigureRequired(m, "fee_amount", "fee_currency"));
        builder.HasIndex(x => new { x.TenantId, x.DeliveryPolicyId, x.ZoneName });
    }
}
