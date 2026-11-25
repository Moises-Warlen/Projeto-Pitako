using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo especializado de Açaí com isolamento multi-tenant.
/// </summary>
public sealed class AcaiBuilderDbContext : PitakoDbContextBase
{
    public const string SchemaName = "acai";

    public AcaiBuilderDbContext(
        DbContextOptions<AcaiBuilderDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<AcaiCupSize> CupSizes => Set<AcaiCupSize>();
    public DbSet<AcaiBaseFlavor> BaseFlavors => Set<AcaiBaseFlavor>();
    public DbSet<AcaiFreeTopping> FreeToppings => Set<AcaiFreeTopping>();
    public DbSet<AcaiPaidAddon> PaidAddons => Set<AcaiPaidAddon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new AcaiCupSizeConfiguration());
        modelBuilder.ApplyConfiguration(new AcaiBaseFlavorConfiguration());
        modelBuilder.ApplyConfiguration(new AcaiFreeToppingConfiguration());
        modelBuilder.ApplyConfiguration(new AcaiPaidAddonConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class AcaiCupSizeConfiguration : IEntityTypeConfiguration<AcaiCupSize>
{
    public void Configure(EntityTypeBuilder<AcaiCupSize> builder)
    {
        builder.ToTable("cup_sizes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.VolumeMl).HasColumnName("volume_ml").IsRequired();
        builder.Property(x => x.FreeToppingsLimit).HasColumnName("free_toppings_limit").IsRequired();
        builder.Property(x => x.MinFreeToppings).HasColumnName("min_free_toppings").IsRequired();
        builder.Property(x => x.CountingMode).HasColumnName("counting_mode").IsRequired();
        builder.Property(x => x.MaxPaidAddonsPerCup).HasColumnName("max_paid_addons_per_cup");
        builder.Property(x => x.LinkedProductId).HasColumnName("linked_product_id");
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.BasePrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("base_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("base_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class AcaiBaseFlavorConfiguration : IEntityTypeConfiguration<AcaiBaseFlavor>
{
    public void Configure(EntityTypeBuilder<AcaiBaseFlavor> builder)
    {
        builder.ToTable("base_flavors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.ExtraPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("extra_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("extra_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class AcaiFreeToppingConfiguration : IEntityTypeConfiguration<AcaiFreeTopping>
{
    public void Configure(EntityTypeBuilder<AcaiFreeTopping> builder)
    {
        builder.ToTable("free_toppings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.MaxPortionsPerCup).HasColumnName("max_portions_per_cup").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id");
        builder.Property(x => x.ConsumptionQuantityPerPortion).HasColumnName("consumption_quantity_per_portion").HasPrecision(18, 4);
        builder.Property(x => x.ConsumptionUnitCode).HasColumnName("consumption_unit_code").HasMaxLength(32);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class AcaiPaidAddonConfiguration : IEntityTypeConfiguration<AcaiPaidAddon>
{
    public void Configure(EntityTypeBuilder<AcaiPaidAddon> builder)
    {
        builder.ToTable("paid_addons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.MaxQuantityPerCup).HasColumnName("max_quantity_per_cup").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id");
        builder.Property(x => x.ConsumptionQuantityPerUnit).HasColumnName("consumption_quantity_per_unit").HasPrecision(18, 4);
        builder.Property(x => x.ConsumptionUnitCode).HasColumnName("consumption_unit_code").HasMaxLength(32);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.AdditionalPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("additional_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("additional_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}
