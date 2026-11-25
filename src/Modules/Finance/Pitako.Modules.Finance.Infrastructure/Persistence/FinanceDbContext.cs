using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Finance.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo Financeiro (contas, fluxo de caixa, conciliação, DRE e gateways).
/// </summary>
public sealed class FinanceDbContext : PitakoDbContextBase
{
    public const string SchemaName = "finance";

    public FinanceDbContext(
        DbContextOptions<FinanceDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<FinancialEntry> FinancialEntries => Set<FinancialEntry>();
    public DbSet<CashFlowLedgerEntry> CashFlowLedger => Set<CashFlowLedgerEntry>();
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<PaymentTransactionRecord> PaymentTransactions => Set<PaymentTransactionRecord>();
    public DbSet<SettlementReconciliationBatch> ReconciliationBatches => Set<SettlementReconciliationBatch>();
    public DbSet<ReconciliationLine> ReconciliationLines => Set<ReconciliationLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new FinancialEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CashFlowLedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentGatewayConfigConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentTransactionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementReconciliationBatchConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationLineConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class FinanceMoneyMapping
{
    public static void Configure<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(p => p.Amount).HasColumnName(amountCol).HasPrecision(18, 2).IsRequired();
        money.Property(p => p.Currency).HasColumnName(currencyCol).HasMaxLength(3).IsRequired();
    }
}

internal sealed class FinancialEntryConfiguration : IEntityTypeConfiguration<FinancialEntry>
{
    public void Configure(EntityTypeBuilder<FinancialEntry> builder)
    {
        builder.ToTable("financial_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.EntryCode).HasColumnName("entry_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        builder.Property(x => x.DreCategory).HasColumnName("dre_category").IsRequired();
        builder.Property(x => x.CounterpartyName).HasColumnName("counterparty_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ReferenceDocument).HasColumnName("reference_document").HasMaxLength(80);
        builder.Property(x => x.DueDateUtc).HasColumnName("due_date_utc").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.SettledAtUtc).HasColumnName("settled_at_utc");
        builder.Property(x => x.PaymentMethodName).HasColumnName("payment_method_name").HasMaxLength(60);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.OriginalAmount, m => FinanceMoneyMapping.Configure(m, "original_amount", "original_currency"));
        builder.OwnsOne(x => x.FeeOrAdjustmentAmount, m => FinanceMoneyMapping.Configure(m, "fee_adjustment_amount", "fee_adjustment_currency"));
        builder.OwnsOne(x => x.SettledAmount, m => FinanceMoneyMapping.Configure(m, "settled_amount", "settled_currency"));

        builder.HasIndex(x => new { x.TenantId, x.EntryCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.DueDateUtc });
    }
}

internal sealed class CashFlowLedgerEntryConfiguration : IEntityTypeConfiguration<CashFlowLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CashFlowLedgerEntry> builder)
    {
        builder.ToTable("cash_flow_ledger");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.FinancialEntryId).HasColumnName("financial_entry_id");
        builder.Property(x => x.Direction).HasColumnName("direction").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
        builder.Property(x => x.PaymentMethodName).HasColumnName("payment_method_name").HasMaxLength(60).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.Amount, m => FinanceMoneyMapping.Configure(m, "amount", "currency"));

        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc });
    }
}

internal sealed class PaymentGatewayConfigConfiguration : IEntityTypeConfiguration<PaymentGatewayConfig>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayConfig> builder)
    {
        builder.ToTable("payment_gateway_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.MerchantAccountCode).HasColumnName("merchant_account_code").HasMaxLength(120).IsRequired();
        builder.Property(x => x.EncryptedSecretBlob).HasColumnName("encrypted_secret_blob").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.DefaultMdrPercent).HasColumnName("default_mdr_percent").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.SettlementDays).HasColumnName("settlement_days").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.MerchantAccountCode }).IsUnique();
    }
}

internal sealed class PaymentTransactionRecordConfiguration : IEntityTypeConfiguration<PaymentTransactionRecord>
{
    public void Configure(EntityTypeBuilder<PaymentTransactionRecord> builder)
    {
        builder.ToTable("payment_transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.GatewayConfigId).HasColumnName("gateway_config_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.OrderOrSaleReference).HasColumnName("order_or_sale_reference").HasMaxLength(80).IsRequired();
        builder.Property(x => x.ExternalTransactionId).HasColumnName("external_transaction_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.NsuCode).HasColumnName("nsu_code").HasMaxLength(64);
        builder.Property(x => x.CardBrand).HasColumnName("card_brand").HasMaxLength(40);
        builder.Property(x => x.CardLast4Digits).HasColumnName("card_last4_digits").HasMaxLength(4);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(400);
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.GrossAmount, m => FinanceMoneyMapping.Configure(m, "gross_amount", "gross_currency"));
        builder.OwnsOne(x => x.GatewayFeeAmount, m => FinanceMoneyMapping.Configure(m, "gateway_fee_amount", "gateway_fee_currency"));
        builder.OwnsOne(x => x.NetAmount, m => FinanceMoneyMapping.Configure(m, "net_amount", "net_currency"));

        builder.HasIndex(x => new { x.TenantId, x.ExternalTransactionId }).IsUnique();
    }
}

internal sealed class SettlementReconciliationBatchConfiguration : IEntityTypeConfiguration<SettlementReconciliationBatch>
{
    public void Configure(EntityTypeBuilder<SettlementReconciliationBatch> builder)
    {
        builder.ToTable("reconciliation_batches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.BatchCode).HasColumnName("batch_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderName).HasColumnName("provider_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ExecutedAtUtc).HasColumnName("executed_at_utc").IsRequired();
        builder.Property(x => x.ExactMatchesCount).HasColumnName("exact_matches_count").IsRequired();
        builder.Property(x => x.DivergencesCount).HasColumnName("divergences_count").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.TotalVarianceAmount, m => FinanceMoneyMapping.Configure(m, "total_variance_amount", "total_variance_currency"));

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.BatchCode }).IsUnique();
    }
}

internal sealed class ReconciliationLineConfiguration : IEntityTypeConfiguration<ReconciliationLine>
{
    public void Configure(EntityTypeBuilder<ReconciliationLine> builder)
    {
        builder.ToTable("reconciliation_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.ExternalTransactionRef).HasColumnName("external_transaction_ref").HasMaxLength(120).IsRequired();
        builder.Property(x => x.MatchStatus).HasColumnName("match_status").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(300);

        builder.OwnsOne(x => x.InternalNetAmount, m => FinanceMoneyMapping.Configure(m, "internal_net_amount", "internal_net_currency"));
        builder.OwnsOne(x => x.StatementNetAmount, m => FinanceMoneyMapping.Configure(m, "statement_net_amount", "statement_net_currency"));
        builder.OwnsOne(x => x.VarianceAmount, m => FinanceMoneyMapping.Configure(m, "variance_amount", "variance_currency"));

        builder.HasIndex(x => new { x.TenantId, x.BatchId });
    }
}
