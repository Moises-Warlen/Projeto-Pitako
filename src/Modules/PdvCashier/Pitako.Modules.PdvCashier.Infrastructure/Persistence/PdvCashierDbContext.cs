using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.PdvCashier.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de PDV e Caixa com isolamento multi-tenant.
/// </summary>
public sealed class PdvCashierDbContext : PitakoDbContextBase
{
    public const string SchemaName = "pdv";

    public PdvCashierDbContext(
        DbContextOptions<PdvCashierDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<CashRegisterSession> CashRegisterSessions => Set<CashRegisterSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<PdvSale> PdvSales => Set<PdvSale>();
    public DbSet<PdvSalePayment> PdvSalePayments => Set<PdvSalePayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new CashRegisterSessionConfiguration());
        modelBuilder.ApplyConfiguration(new CashMovementConfiguration());
        modelBuilder.ApplyConfiguration(new PdvSaleConfiguration());
        modelBuilder.ApplyConfiguration(new PdvSalePaymentConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class PdvMoneyMapping
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

internal sealed class CashRegisterSessionConfiguration : IEntityTypeConfiguration<CashRegisterSession>
{
    public void Configure(EntityTypeBuilder<CashRegisterSession> builder)
    {
        builder.ToTable("cash_register_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.TerminalCode).HasColumnName("terminal_code").HasMaxLength(64).IsRequired();
        builder.Property(x => x.OpenedByUserId).HasColumnName("opened_by_user_id").IsRequired();
        builder.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.Property(x => x.ClosingNotes).HasColumnName("closing_notes").HasMaxLength(400);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.OpeningBalance, m => PdvMoneyMapping.ConfigureRequired(m, "opening_balance_amount", "opening_balance_currency"));
        builder.OwnsOne(x => x.ExpectedCashBalance, m => PdvMoneyMapping.ConfigureRequired(m, "expected_cash_amount", "expected_cash_currency"));
        builder.OwnsOne(x => x.CountedCashBalance, m => PdvMoneyMapping.ConfigureOptional(m, "counted_cash_amount", "counted_cash_currency"));
        builder.OwnsOne(x => x.CashVariance, m => PdvMoneyMapping.ConfigureOptional(m, "cash_variance_amount", "cash_variance_currency"));

        builder.HasMany(x => x.Movements).WithOne().HasForeignKey(m => m.CashRegisterSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.TerminalCode, x.Status });
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CashRegisterSessionId).HasColumnName("cash_register_session_id").IsRequired();
        builder.Property(x => x.MovementType).HasColumnName("movement_type").IsRequired();
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300).IsRequired();
        builder.Property(x => x.PdvSaleId).HasColumnName("pdv_sale_id");
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();

        builder.OwnsOne(x => x.Amount, m => PdvMoneyMapping.ConfigureRequired(m, "amount", "currency"));

        builder.HasIndex(x => new { x.TenantId, x.CashRegisterSessionId, x.OccurredAtUtc });
    }
}

internal sealed class PdvSaleConfiguration : IEntityTypeConfiguration<PdvSale>
{
    public void Configure(EntityTypeBuilder<PdvSale> builder)
    {
        builder.ToTable("pdv_sales");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CashRegisterSessionId).HasColumnName("cash_register_session_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DiscountReason).HasColumnName("discount_reason").HasMaxLength(240);
        builder.Property(x => x.DiscountAuthorizedByUserId).HasColumnName("discount_authorized_by_user_id");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.OperatorUserId).HasColumnName("operator_user_id").IsRequired();
        builder.Property(x => x.SettledAtUtc).HasColumnName("settled_at_utc").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(300);
        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.GrossAmount, m => PdvMoneyMapping.ConfigureRequired(m, "gross_amount", "gross_currency"));
        builder.OwnsOne(x => x.DiscountAmount, m => PdvMoneyMapping.ConfigureRequired(m, "discount_amount", "discount_currency"));
        builder.OwnsOne(x => x.NetAmount, m => PdvMoneyMapping.ConfigureRequired(m, "net_amount", "net_currency"));

        builder.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.PdvSaleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.CashRegisterSessionId, x.SettledAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}

internal sealed class PdvSalePaymentConfiguration : IEntityTypeConfiguration<PdvSalePayment>
{
    public void Configure(EntityTypeBuilder<PdvSalePayment> builder)
    {
        builder.ToTable("pdv_sale_payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.PdvSaleId).HasColumnName("pdv_sale_id").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").IsRequired();
        builder.Property(x => x.AuthorizationCode).HasColumnName("authorization_code").HasMaxLength(80);

        builder.OwnsOne(x => x.AllocatedAmount, m => PdvMoneyMapping.ConfigureRequired(m, "allocated_amount", "allocated_currency"));
        builder.OwnsOne(x => x.TenderedAmount, m => PdvMoneyMapping.ConfigureRequired(m, "tendered_amount", "tendered_currency"));
        builder.OwnsOne(x => x.ChangeAmount, m => PdvMoneyMapping.ConfigureRequired(m, "change_amount", "change_currency"));

        builder.HasIndex(x => new { x.TenantId, x.PdvSaleId });
    }
}
