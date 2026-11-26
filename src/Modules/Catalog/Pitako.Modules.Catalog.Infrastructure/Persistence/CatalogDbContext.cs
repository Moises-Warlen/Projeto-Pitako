using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Catalog.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do catálogo de produtos e categorias com filtros globais multi-tenant.
/// </summary>
public sealed class CatalogDbContext : PitakoDbContextBase
{
    public const string SchemaName = "catalog";

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariation> ProductVariations => Set<ProductVariation>();
    public DbSet<ProductModifierLink> ProductModifierLinks => Set<ProductModifierLink>();
    public DbSet<VariationModifierRule> VariationModifierRules => Set<VariationModifierRule>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<ModifierOption> ModifierOptions => Set<ModifierOption>();
    public DbSet<Combo> Combos => Set<Combo>();
    public DbSet<ComboItem> ComboItems => Set<ComboItem>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionTarget> PromotionTargets => Set<PromotionTarget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductImageConfiguration());
        modelBuilder.ApplyConfiguration(new ProductVariationConfiguration());
        modelBuilder.ApplyConfiguration(new ProductModifierLinkConfiguration());
        modelBuilder.ApplyConfiguration(new VariationModifierRuleConfiguration());
        modelBuilder.ApplyConfiguration(new ModifierGroupConfiguration());
        modelBuilder.ApplyConfiguration(new ModifierOptionConfiguration());
        modelBuilder.ApplyConfiguration(new ComboConfiguration());
        modelBuilder.ApplyConfiguration(new ComboItemConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionTargetConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class MoneyMappingHelper
{
    public static void ConfigureMoney<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(m => m.Amount).HasColumnName(amountCol).HasPrecision(18, 2).IsRequired();
        money.Property(m => m.Currency).HasColumnName(currencyCol).HasMaxLength(3).IsRequired();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(500);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.SegmentTemplateCode).HasColumnName("segment_template_code").HasMaxLength(64);
        builder.Property(x => x.RuleNotes).HasColumnName("rule_notes").HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
        builder.Property(x => x.SaleMode).HasColumnName("sale_mode").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.IsFeatured).HasColumnName("is_featured").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsNew).HasColumnName("is_new").IsRequired();
        builder.Property(x => x.IsRecommended).HasColumnName("is_recommended").IsRequired();
        builder.Property(x => x.PricingStrategy).HasColumnName("pricing_strategy").IsRequired();
        builder.Property(x => x.TagsCsv).HasColumnName("tags_csv").HasMaxLength(300);
        builder.Property(x => x.PreparationMinutes).HasColumnName("preparation_minutes");
        builder.Property(x => x.CustomTemplateKey).HasColumnName("custom_template_key").HasMaxLength(80);
        builder.Property(x => x.AllowsNotes).HasColumnName("allows_notes").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.BasePrice, m => MoneyMappingHelper.ConfigureMoney(m, "base_price_amount", "base_price_currency"));

        builder.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Variations).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Variations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.ModifierLinks).WithOne().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.ModifierLinks).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.VariationRules).WithOne().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.VariationRules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.CategoryId, x.DisplayOrder });
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(500).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.DisplayOrder });
    }
}

internal sealed class ProductVariationConfiguration : IEntityTypeConfiguration<ProductVariation>
{
    public void Configure(EntityTypeBuilder<ProductVariation> builder)
    {
        builder.ToTable("product_variations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300);
        builder.Property(x => x.MeasureAmount).HasColumnName("measure_amount").HasPrecision(18, 2);
        builder.Property(x => x.MeasureUnit).HasColumnName("measure_unit").HasMaxLength(24);
        builder.Property(x => x.MaxFlavors).HasColumnName("max_flavors").IsRequired();
        builder.OwnsOne(x => x.Price, m => MoneyMappingHelper.ConfigureMoney(m, "price_amount", "price_currency"));
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.DisplayOrder });
    }
}

internal sealed class VariationModifierRuleConfiguration : IEntityTypeConfiguration<VariationModifierRule>
{
    public void Configure(EntityTypeBuilder<VariationModifierRule> builder)
    {
        builder.ToTable("variation_modifier_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.ProductVariationId).HasColumnName("product_variation_id");
        builder.Property(x => x.ModifierGroupId).HasColumnName("modifier_group_id").IsRequired();
        builder.Property(x => x.MinSelections).HasColumnName("min_selections").IsRequired();
        builder.Property(x => x.MaxSelections).HasColumnName("max_selections").IsRequired();
        builder.Property(x => x.FreeAllowance).HasColumnName("free_allowance").IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        builder.Property(x => x.IsHidden).HasColumnName("is_hidden").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.ProductVariationId, x.ModifierGroupId }).IsUnique();
    }
}

internal sealed class ProductModifierLinkConfiguration : IEntityTypeConfiguration<ProductModifierLink>
{
    public void Configure(EntityTypeBuilder<ProductModifierLink> builder)
    {
        builder.ToTable("product_modifier_links");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.ModifierGroupId).HasColumnName("modifier_group_id").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.ModifierGroupId }).IsUnique();
    }
}

internal sealed class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> builder)
    {
        builder.ToTable("modifier_groups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.MinSelections).HasColumnName("min_selections").IsRequired();
        builder.Property(x => x.MaxSelections).HasColumnName("max_selections").IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.SelectionKind).HasColumnName("selection_kind").IsRequired();
        builder.Property(x => x.FreeAllowance).HasColumnName("free_allowance").IsRequired();
        builder.Property(x => x.CountingMode).HasColumnName("counting_mode").IsRequired();
        builder.Property(x => x.CategoryTag).HasColumnName("category_tag").HasMaxLength(64);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class ModifierOptionConfiguration : IEntityTypeConfiguration<ModifierOption>
{
    public void Configure(EntityTypeBuilder<ModifierOption> builder)
    {
        builder.ToTable("modifier_options");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ModifierGroupId).HasColumnName("modifier_group_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
        builder.Property(x => x.MaxQuantity).HasColumnName("max_quantity").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsAvailable).HasColumnName("is_available").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300);
        builder.Property(x => x.SupplyItemId).HasColumnName("supply_item_id");
        builder.Property(x => x.ConsumptionQuantity).HasColumnName("consumption_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConsumptionUnitCode).HasColumnName("consumption_unit_code").HasMaxLength(24);
        builder.Property(x => x.LossPercent).HasColumnName("loss_percent").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.ReplacesLabel).HasColumnName("replaces_label").HasMaxLength(140);
        builder.Property(x => x.IncompatibleOptionIdsCsv).HasColumnName("incompatible_option_ids_csv").HasMaxLength(500);
        builder.OwnsOne(x => x.AdditionalPrice, m => MoneyMappingHelper.ConfigureMoney(m, "additional_price_amount", "additional_price_currency"));
        builder.HasIndex(x => new { x.TenantId, x.ModifierGroupId, x.DisplayOrder });
    }
}

internal sealed class ComboConfiguration : IEntityTypeConfiguration<Combo>
{
    public void Configure(EntityTypeBuilder<Combo> builder)
    {
        builder.ToTable("combos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(800);
        builder.Property(x => x.PrimaryImageUrl).HasColumnName("primary_image_url").HasMaxLength(500);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.IsFeatured).HasColumnName("is_featured").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.ComboPrice, m => MoneyMappingHelper.ConfigureMoney(m, "combo_price_amount", "combo_price_currency"));

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ComboId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class ComboItemConfiguration : IEntityTypeConfiguration<ComboItem>
{
    public void Configure(EntityTypeBuilder<ComboItem> builder)
    {
        builder.ToTable("combo_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ComboId).HasColumnName("combo_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(x => x.VariationId).HasColumnName("variation_id");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(300);
        builder.HasIndex(x => new { x.TenantId, x.ComboId });
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.DiscountType).HasColumnName("discount_type").IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnName("discount_value").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.StartsAtUtc).HasColumnName("starts_at_utc").IsRequired();
        builder.Property(x => x.EndsAtUtc).HasColumnName("ends_at_utc").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CanCombineWithCoupons).HasColumnName("can_combine_with_coupons").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Targets).WithOne().HasForeignKey(t => t.PromotionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

internal sealed class PromotionTargetConfiguration : IEntityTypeConfiguration<PromotionTarget>
{
    public void Configure(EntityTypeBuilder<PromotionTarget> builder)
    {
        builder.ToTable("promotion_targets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.PromotionId).HasColumnName("promotion_id").IsRequired();
        builder.Property(x => x.TargetType).HasColumnName("target_type").IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.PromotionId, x.TargetType, x.TargetId }).IsUnique();
    }
}
