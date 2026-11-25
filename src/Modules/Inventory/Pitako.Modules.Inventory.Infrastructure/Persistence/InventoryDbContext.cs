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
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockLot> StockLots => Set<StockLot>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionIngredientConsumption> ProductionIngredientConsumptions => Set<ProductionIngredientConsumption>();

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
        modelBuilder.ApplyConfiguration(new StockBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new StockLotConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseReceiptItemConfiguration());
        modelBuilder.ApplyConfiguration(new ProductionOrderConfiguration());
        modelBuilder.ApplyConfiguration(new ProductionIngredientConsumptionConfiguration());

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

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stock_balances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.MinimumStockQuantity).HasColumnName("minimum_stock_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.AllowNegativeStock).HasColumnName("allow_negative_stock").IsRequired();
        builder.Property(x => x.AutoDeductOnOrder).HasColumnName("auto_deduct_on_order").IsRequired();
        builder.Property(x => x.BlockExpiredLots).HasColumnName("block_expired_lots").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Lots).WithOne().HasForeignKey(l => l.StockBalanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lots).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Movements).WithOne().HasForeignKey(m => m.StockBalanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId }).IsUnique();
    }
}

internal sealed class StockLotConfiguration : IEntityTypeConfiguration<StockLot>
{
    public void Configure(EntityTypeBuilder<StockLot> builder)
    {
        builder.ToTable("stock_lots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StockBalanceId).HasColumnName("stock_balance_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InitialQuantity).HasColumnName("initial_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.RemainingQuantity).HasColumnName("remaining_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ManufacturedAtUtc).HasColumnName("manufactured_at_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId, x.ExpiresAtUtc });
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StockBalanceId).HasColumnName("stock_balance_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.StockLotId).HasColumnName("stock_lot_id");
        builder.Property(x => x.MovementType).HasColumnName("movement_type").IsRequired();
        builder.Property(x => x.QuantityDelta).HasColumnName("quantity_delta").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PreviousQuantity).HasColumnName("previous_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ResultingQuantity).HasColumnName("resulting_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ReferenceType).HasColumnName("reference_type").HasMaxLength(64);
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.ReferenceType, x.ReferenceId });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}

internal sealed class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.ToTable("purchase_receipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(80);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.ReceivedByUserId).HasColumnName("received_by_user_id");
        builder.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.TotalAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("total_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("total_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PurchaseReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.ReceivedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}

internal sealed class PurchaseReceiptItemConfiguration : IEntityTypeConfiguration<PurchaseReceiptItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.ToTable("purchase_receipt_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.PurchaseReceiptId).HasColumnName("purchase_receipt_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.SupplyNameSnapshot).HasColumnName("supply_name_snapshot").HasMaxLength(180).IsRequired();
        builder.Property(x => x.PurchasedQuantity).HasColumnName("purchased_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PurchaseUnitCode).HasColumnName("purchase_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreditedStockQuantity).HasColumnName("credited_stock_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.StockUnitCode).HasColumnName("stock_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(64);
        builder.Property(x => x.ManufacturedAtUtc).HasColumnName("manufactured_at_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");

        builder.OwnsOne(x => x.UnitPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("unit_price_currency").HasMaxLength(3).IsRequired();
        });
        builder.OwnsOne(x => x.TotalCost, m =>
        {
            m.Property(p => p.Amount).HasColumnName("total_cost_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("total_cost_currency").HasMaxLength(3).IsRequired();
        });
    }
}

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("production_orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TechnicalSheetId).HasColumnName("technical_sheet_id").IsRequired();
        builder.Property(x => x.TechnicalSheetNameSnapshot).HasColumnName("technical_sheet_name_snapshot").HasMaxLength(180).IsRequired();
        builder.Property(x => x.TechnicalSheetVersion).HasColumnName("technical_sheet_version").IsRequired();
        builder.Property(x => x.OutputSupplyItemId).HasColumnName("output_supply_item_id");
        builder.Property(x => x.BatchMultiplier).HasColumnName("batch_multiplier").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ProducedQuantity).HasColumnName("produced_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.YieldUnitCode).HasColumnName("yield_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(64);
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.TotalProductionCost).HasColumnName("total_production_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitProductionCost).HasColumnName("unit_production_cost").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.ExecutedByUserId).HasColumnName("executed_by_user_id");
        builder.Property(x => x.ExecutedAtUtc).HasColumnName("executed_at_utc").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Consumptions).WithOne().HasForeignKey(c => c.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Consumptions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.TechnicalSheetId, x.ExecutedAtUtc });
    }
}

internal sealed class ProductionIngredientConsumptionConfiguration : IEntityTypeConfiguration<ProductionIngredientConsumption>
{
    public void Configure(EntityTypeBuilder<ProductionIngredientConsumption> builder)
    {
        builder.ToTable("production_consumptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductionOrderId).HasColumnName("production_order_id").IsRequired();
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id").IsRequired();
        builder.Property(x => x.SupplyNameSnapshot).HasColumnName("supply_name_snapshot").HasMaxLength(180).IsRequired();
        builder.Property(x => x.QuantityConsumedInStockUnit).HasColumnName("quantity_consumed_in_stock_unit").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.StockUnitCode).HasColumnName("stock_unit_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CalculatedCost).HasColumnName("calculated_cost").HasPrecision(18, 4).IsRequired();
    }
}
