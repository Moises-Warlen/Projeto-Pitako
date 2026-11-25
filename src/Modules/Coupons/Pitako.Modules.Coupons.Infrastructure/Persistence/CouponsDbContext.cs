using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Coupons.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Coupons.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Cupons com isolamento multi-tenant.
/// </summary>
public sealed class CouponsDbContext : PitakoDbContextBase
{
    public const string SchemaName = "coupons";

    public CouponsDbContext(
        DbContextOptions<CouponsDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponEligibilityRule> CouponEligibilityRules => Set<CouponEligibilityRule>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new CouponConfiguration());
        modelBuilder.ApplyConfiguration(new CouponEligibilityRuleConfiguration());
        modelBuilder.ApplyConfiguration(new CouponRedemptionConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(240);
        builder.Property(x => x.DiscountType).HasColumnName("discount_type").IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnName("discount_value").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ValidFromUtc).HasColumnName("valid_from_utc").IsRequired();
        builder.Property(x => x.ValidUntilUtc).HasColumnName("valid_until_utc").IsRequired();
        builder.Property(x => x.MaxTotalUses).HasColumnName("max_total_uses");
        builder.Property(x => x.MaxUsesPerCustomer).HasColumnName("max_uses_per_customer");
        builder.Property(x => x.CanCombineWithPromotions).HasColumnName("can_combine_with_promotions").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.MinimumOrderAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("minimum_order_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("minimum_order_currency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(x => x.MaxDiscountAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("max_discount_amount").HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("max_discount_currency").HasMaxLength(3);
        });

        builder.HasMany(x => x.EligibilityRules).WithOne().HasForeignKey(r => r.CouponId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.EligibilityRules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Redemptions).WithOne().HasForeignKey(r => r.CouponId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Redemptions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

internal sealed class CouponEligibilityRuleConfiguration : IEntityTypeConfiguration<CouponEligibilityRule>
{
    public void Configure(EntityTypeBuilder<CouponEligibilityRule> builder)
    {
        builder.ToTable("coupon_eligibility_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CouponId).HasColumnName("coupon_id").IsRequired();
        builder.Property(x => x.TargetType).HasColumnName("target_type").IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CouponId });
    }
}

internal sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CouponId).HasColumnName("coupon_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.CustomerIdentifier).HasColumnName("customer_identifier").HasMaxLength(160).IsRequired();
        builder.Property(x => x.RedeemedAtUtc).HasColumnName("redeemed_at_utc").IsRequired();
        builder.Property(x => x.ReversedAtUtc).HasColumnName("reversed_at_utc");

        builder.OwnsOne(x => x.DiscountApplied, m =>
        {
            m.Property(p => p.Amount).HasColumnName("discount_applied_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("discount_applied_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.CouponId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.CouponId, x.CustomerIdentifier });
    }
}
