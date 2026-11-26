using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Orders.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Carrinho e Pedidos com isolamento multi-tenant.
/// </summary>
public sealed class OrdersDbContext : PitakoDbContextBase
{
    public const string SchemaName = "orders";

    public OrdersDbContext(
        DbContextOptions<OrdersDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<ShoppingCartItem> ShoppingCartItems => Set<ShoppingCartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new ShoppingCartConfiguration());
        modelBuilder.ApplyConfiguration(new ShoppingCartItemConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new OrderStatusHistoryConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class OrdersMoneyMapping
{
    public static void Configure<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(p => p.Amount).HasColumnName(amountCol).HasPrecision(18, 2).IsRequired();
        money.Property(p => p.Currency).HasColumnName(currencyCol).HasMaxLength(3).IsRequired();
    }
}

internal sealed class ShoppingCartConfiguration : IEntityTypeConfiguration<ShoppingCart>
{
    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        builder.ToTable("shopping_carts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CartSessionKey).HasColumnName("cart_session_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.CustomerIdentifier).HasColumnName("customer_identifier").HasMaxLength(160);
        builder.Property(x => x.AppliedCouponCode).HasColumnName("applied_coupon_code").HasMaxLength(40);
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.DeliveryFee, m => OrdersMoneyMapping.Configure(m, "delivery_fee_amount", "delivery_fee_currency"));
        builder.OwnsOne(x => x.ItemsSubtotal, m => OrdersMoneyMapping.Configure(m, "items_subtotal_amount", "items_subtotal_currency"));
        builder.OwnsOne(x => x.ItemsDiscount, m => OrdersMoneyMapping.Configure(m, "items_discount_amount", "items_discount_currency"));
        builder.OwnsOne(x => x.DeliveryDiscount, m => OrdersMoneyMapping.Configure(m, "delivery_discount_amount", "delivery_discount_currency"));
        builder.OwnsOne(x => x.TotalAmount, m => OrdersMoneyMapping.Configure(m, "total_amount", "total_currency"));

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ShoppingCartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.CartSessionKey }).IsUnique();
    }
}

internal sealed class ShoppingCartItemConfiguration : IEntityTypeConfiguration<ShoppingCartItem>
{
    public void Configure(EntityTypeBuilder<ShoppingCartItem> builder)
    {
        builder.ToTable("shopping_cart_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ShoppingCartId).HasColumnName("shopping_cart_id").IsRequired();
        builder.Property(x => x.ItemKind).HasColumnName("item_kind").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.CategoryId).HasColumnName("category_id");
        builder.Property(x => x.VariationId).HasColumnName("variation_id");
        builder.Property(x => x.AcaiCupSizeId).HasColumnName("acai_cup_size_id");
        builder.Property(x => x.WeighingRecordId).HasColumnName("weighing_record_id");
        builder.Property(x => x.ItemTitle).HasColumnName("item_title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomizationSummaryText).HasColumnName("customization_summary_text").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SelectionsPayloadJson).HasColumnName("selections_payload_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.CustomizationFingerprint).HasColumnName("customization_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(x => x.HasNonCombinablePromotion).HasColumnName("has_non_combinable_promotion").IsRequired();
        builder.Property(x => x.CustomerNotes).HasColumnName("customer_notes").HasMaxLength(400);
        builder.Property(x => x.AddedAtUtc).HasColumnName("added_at_utc").IsRequired();

        builder.OwnsOne(x => x.UnitPrice, m => OrdersMoneyMapping.Configure(m, "unit_price_amount", "unit_price_currency"));
        builder.OwnsOne(x => x.LineSubtotal, m => OrdersMoneyMapping.Configure(m, "line_subtotal_amount", "line_subtotal_currency"));

        builder.HasIndex(x => new { x.TenantId, x.ShoppingCartId, x.CustomizationFingerprint });
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.OrderNumber).HasColumnName("order_number").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").IsRequired();
        builder.Property(x => x.FulfillmentMode).HasColumnName("fulfillment_mode").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.CustomerIdentifier).HasColumnName("customer_identifier").HasMaxLength(160);
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(40);
        builder.Property(x => x.DeliveryAddressSummary).HasColumnName("delivery_address_summary").HasMaxLength(400);
        builder.Property(x => x.AppliedCouponCode).HasColumnName("applied_coupon_code").HasMaxLength(40);
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(40);
        builder.Property(x => x.LgpdConsentAccepted).HasColumnName("lgpd_consent_accepted").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(300);
        builder.Property(x => x.PlacedAtUtc).HasColumnName("placed_at_utc").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.ItemsSubtotal, m => OrdersMoneyMapping.Configure(m, "items_subtotal_amount", "items_subtotal_currency"));
        builder.OwnsOne(x => x.ItemsDiscount, m => OrdersMoneyMapping.Configure(m, "items_discount_amount", "items_discount_currency"));
        builder.OwnsOne(x => x.DeliveryFee, m => OrdersMoneyMapping.Configure(m, "delivery_fee_amount", "delivery_fee_currency"));
        builder.OwnsOne(x => x.DeliveryDiscount, m => OrdersMoneyMapping.Configure(m, "delivery_discount_amount", "delivery_discount_currency"));
        builder.OwnsOne(x => x.NetDeliveryFee, m => OrdersMoneyMapping.Configure(m, "net_delivery_fee_amount", "net_delivery_fee_currency"));
        builder.OwnsOne(x => x.TotalAmount, m => OrdersMoneyMapping.Configure(m, "total_amount", "total_currency"));

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Status, x.PlacedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.ItemKind).HasColumnName("item_kind").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.CategoryId).HasColumnName("category_id");
        builder.Property(x => x.VariationId).HasColumnName("variation_id");
        builder.Property(x => x.AcaiCupSizeId).HasColumnName("acai_cup_size_id");
        builder.Property(x => x.WeighingRecordId).HasColumnName("weighing_record_id");
        builder.Property(x => x.ItemTitle).HasColumnName("item_title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomizationSummaryText).HasColumnName("customization_summary_text").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SelectionsPayloadJson).HasColumnName("selections_payload_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(x => x.CustomerNotes).HasColumnName("customer_notes").HasMaxLength(400);

        builder.OwnsOne(x => x.UnitPrice, m => OrdersMoneyMapping.Configure(m, "unit_price_amount", "unit_price_currency"));
        builder.OwnsOne(x => x.LineSubtotal, m => OrdersMoneyMapping.Configure(m, "line_subtotal_amount", "line_subtotal_currency"));

        builder.HasIndex(x => new { x.TenantId, x.ProductId });
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_histories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status");
        builder.Property(x => x.ToStatus).HasColumnName("to_status").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OrderId, x.ChangedAtUtc });
    }
}
