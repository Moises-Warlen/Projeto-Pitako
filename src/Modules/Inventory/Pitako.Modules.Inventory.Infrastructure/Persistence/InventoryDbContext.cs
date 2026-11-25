using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Inventory.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Insumos, Fichas Técnicas e Estoque com isolamento multi-tenant.
/// </summary>
public sealed class InventoryDbContext : PitakoDbContextBase
{
    public const string SchemaName = "inventory";

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<TenantCustomUnit> CustomUnits => Set<TenantCustomUnit>();
    public DbSet<SupplyItem> SupplyItems => Set<SupplyItem>();
    public DbSet<SupplyPriceHistory> SupplyPriceHistories => Set<SupplyPriceHistory>();
    public DbSet<TechnicalSheet> TechnicalSheets => Set<TechnicalSheet>();
    public DbSet<TechnicalSheetIngredient> TechnicalSheetIngredients => Set<TechnicalSheetIngredient>();
    public DbSet<TechnicalSheetPackaging> TechnicalSheetPackagings => Set<TechnicalSheetPackaging>();
    public DbSet<TechnicalSheetCostSnapshot> TechnicalSheetCostSnapshots => Set<TechnicalSheetCostSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new TenantCustomUnitConfiguration());
        modelBuilder.ApplyConfiguration(new SupplyItemConfiguration());
        modelBuilder.ApplyConfiguration(new SupplyPriceHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new TechnicalSheetConfiguration());
        modelBuilder.ApplyConfiguration(new TechnicalSheetIngredientConfiguration());
        modelBuilder.ApplyConfiguration(new TechnicalSheetPackagingConfiguration());
        modelBuilder.ApplyConfiguration(new TechnicalSheetCostSnapshotConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class TenantCustomUnitConfiguration : IEntityTypeConfiguration<TenantCustomUnit>
{
    public void Configure(EntityTypeBuilder<TenantCustomUnit> builder)
    {
        builder.ToTable("custom_units");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Dimension).HasColumnName("dimension").IsRequired();
        builder.Property(x => x.FactorToBaseUnit).HasColumnName("factor_to_base_unit").HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

internal sealed class SupplyItemConfiguration : IEntityTypeConfiguration<SupplyItem>
{
    public void Configure(EntityTypeBuilder<SupplyItem> builder)
    {
        builder.ToTable("supply_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
        builder.Property(x => x.PurchaseUnitCode).HasColumnName("purchase_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PurchaseQuantity).HasColumnName("purchase_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ContentUnitCode).HasColumnName("content_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentQuantityPerPackage).HasColumnName("content_quantity_per_package").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(160);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.PurchasePrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("purchase_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("purchase_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasMany(x => x.PriceHistory).WithOne().HasForeignKey(h => h.SupplyItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.PriceHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}

internal sealed class SupplyPriceHistoryConfiguration : IEntityTypeConfiguration<SupplyPriceHistory>
{
    public void Configure(EntityTypeBuilder<SupplyPriceHistory> builder)
    {
        builder.ToTable("supply_price_histories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(160);
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(240).IsRequired();
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").IsRequired();

        builder.OwnsOne(x => x.PreviousPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("previous_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("previous_price_currency").HasMaxLength(3).IsRequired();
        });
        builder.OwnsOne(x => x.NewPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("new_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("new_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId });
    }
}

internal sealed class TechnicalSheetConfiguration : IEntityTypeConfiguration<TechnicalSheet>
{
    public void Configure(EntityTypeBuilder<TechnicalSheet> builder)
    {
        builder.ToTable("technical_sheets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.YieldQuantity).HasColumnName("yield_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.YieldUnitCode).HasColumnName("yield_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.IngredientsTotalCost).HasColumnName("ingredients_total_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PackagingsTotalCost).HasColumnName("packagings_total_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.CostPerPortion).HasColumnName("cost_per_portion").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Ingredients).WithOne().HasForeignKey(i => i.TechnicalSheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Ingredients).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Packagings).WithOne().HasForeignKey(p => p.TechnicalSheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Packagings).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.CostHistory).WithOne().HasForeignKey(c => c.TechnicalSheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.CostHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.ProductId });
    }
}

internal sealed class TechnicalSheetIngredientConfiguration : IEntityTypeConfiguration<TechnicalSheetIngredient>
{
    public void Configure(EntityTypeBuilder<TechnicalSheetIngredient> builder)
    {
        builder.ToTable("technical_sheet_ingredients");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TechnicalSheetId).HasColumnName("technical_sheet_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.SupplyItemNameSnapshot).HasColumnName("supply_item_name_snapshot").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CalculatedCost).HasColumnName("calculated_cost").HasPrecision(18, 4).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId });
    }
}

internal sealed class TechnicalSheetPackagingConfiguration : IEntityTypeConfiguration<TechnicalSheetPackaging>
{
    public void Configure(EntityTypeBuilder<TechnicalSheetPackaging> builder)
    {
        builder.ToTable("technical_sheet_packagings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TechnicalSheetId).HasColumnName("technical_sheet_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CalculatedCost).HasColumnName("calculated_cost").HasPrecision(18, 4).IsRequired();
    }
}

internal sealed class TechnicalSheetCostSnapshotConfiguration : IEntityTypeConfiguration<TechnicalSheetCostSnapshot>
{
    public void Configure(EntityTypeBuilder<TechnicalSheetCostSnapshot> builder)
    {
        builder.ToTable("technical_sheet_cost_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TechnicalSheetId).HasColumnName("technical_sheet_id").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.IngredientsCost).HasColumnName("ingredients_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PackagingsCost).HasColumnName("packagings_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.YieldQuantity).HasColumnName("yield_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.CostPerPortion).HasColumnName("cost_per_portion").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TriggerReason).HasColumnName("trigger_reason").HasMaxLength(300).IsRequired();
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TechnicalSheetId, x.Version });
    }
}
