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
    public DbSet<DeliveryCourier> Couriers => Set<DeliveryCourier>();
    public DbSet<DeliveryDispatchRun> DispatchRuns => Set<DeliveryDispatchRun>();
    public DbSet<DeliveryRunStop> RunStops => Set<DeliveryRunStop>();
    public DbSet<CourierGpsPing> GpsPings => Set<CourierGpsPing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new DeliveryPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryDistanceTierConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryZoneRuleConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryCourierConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryDispatchRunConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryRunStopConfiguration());
        modelBuilder.ApplyConfiguration(new CourierGpsPingConfiguration());

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

internal sealed class DeliveryCourierConfiguration : IEntityTypeConfiguration<DeliveryCourier>
{
    public void Configure(EntityTypeBuilder<DeliveryCourier> builder)
    {
        builder.ToTable("delivery_couriers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.LinkedUserId).HasColumnName("linked_user_id");
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").IsRequired();
        builder.Property(x => x.LicensePlate).HasColumnName("license_plate").HasMaxLength(20);
        builder.Property(x => x.CommissionMode).HasColumnName("commission_mode").IsRequired();
        builder.Property(x => x.FreightCommissionPercentage).HasColumnName("freight_commission_percentage").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.Availability).HasColumnName("availability").IsRequired();
        builder.Property(x => x.LastKnownLatitude).HasColumnName("last_known_latitude");
        builder.Property(x => x.LastKnownLongitude).HasColumnName("last_known_longitude");
        builder.Property(x => x.LastLocationAtUtc).HasColumnName("last_location_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.FixedCommission, m => DeliveryMoneyMapping.ConfigureRequired(m, "fixed_commission_amount", "fixed_commission_currency"));
        builder.HasIndex(x => new { x.TenantId, x.Availability });
    }
}

internal sealed class DeliveryDispatchRunConfiguration : IEntityTypeConfiguration<DeliveryDispatchRun>
{
    public void Configure(EntityTypeBuilder<DeliveryDispatchRun> builder)
    {
        builder.ToTable("delivery_dispatch_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CourierId).HasColumnName("courier_id").IsRequired();
        builder.Property(x => x.RunCode).HasColumnName("run_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.TotalEstimatedDistanceKm).HasColumnName("total_estimated_distance_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").IsRequired();
        builder.Property(x => x.DispatchedAtUtc).HasColumnName("dispatched_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(400);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.TotalCommission, m => DeliveryMoneyMapping.ConfigureRequired(m, "total_commission_amount", "total_commission_currency"));

        builder.HasMany(x => x.Stops).WithOne().HasForeignKey(s => s.DeliveryDispatchRunId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.CourierId, x.AssignedAtUtc });
    }
}

internal sealed class DeliveryRunStopConfiguration : IEntityTypeConfiguration<DeliveryRunStop>
{
    public void Configure(EntityTypeBuilder<DeliveryRunStop> builder)
    {
        builder.ToTable("delivery_run_stops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DeliveryDispatchRunId).HasColumnName("delivery_dispatch_run_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.OrderNumberSnapshot).HasColumnName("order_number_snapshot").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CustomerNameSnapshot).HasColumnName("customer_name_snapshot").HasMaxLength(160).IsRequired();
        builder.Property(x => x.DestinationAddress).HasColumnName("destination_address").HasMaxLength(400).IsRequired();
        builder.Property(x => x.DestinationLatitude).HasColumnName("destination_latitude").IsRequired();
        builder.Property(x => x.DestinationLongitude).HasColumnName("destination_longitude").IsRequired();
        builder.Property(x => x.StopSequence).HasColumnName("stop_sequence").IsRequired();
        builder.Property(x => x.LegDistanceKm).HasColumnName("leg_distance_km").HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(300);

        builder.OwnsOne(x => x.DeliveryFeeSnapshot, m => DeliveryMoneyMapping.ConfigureRequired(m, "delivery_fee_amount", "delivery_fee_currency"));
        builder.OwnsOne(x => x.CourierCommission, m => DeliveryMoneyMapping.ConfigureRequired(m, "courier_commission_amount", "courier_commission_currency"));

        builder.HasIndex(x => new { x.TenantId, x.DeliveryDispatchRunId, x.StopSequence });
    }
}

internal sealed class CourierGpsPingConfiguration : IEntityTypeConfiguration<CourierGpsPing>
{
    public void Configure(EntityTypeBuilder<CourierGpsPing> builder)
    {
        builder.ToTable("courier_gps_pings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CourierId).HasColumnName("courier_id").IsRequired();
        builder.Property(x => x.ActiveDispatchRunId).HasColumnName("active_dispatch_run_id");
        builder.Property(x => x.Latitude).HasColumnName("latitude").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").IsRequired();
        builder.Property(x => x.SpeedKmh).HasColumnName("speed_kmh").HasPrecision(6, 2);
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.CourierId, x.RecordedAtUtc });
    }
}
