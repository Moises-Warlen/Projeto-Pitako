using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.MasterPanel.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do Painel Master (planos, assinaturas, mensalidades, clientes SaaS, suporte e configurações globais).
/// </summary>
public sealed class MasterPanelDbContext : DbContext
{
    public const string SchemaName = "master";
    private readonly IClock _clock;

    public MasterPanelDbContext(DbContextOptions<MasterPanelDbContext> options, IClock clock)
        : base(options)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public DbSet<CommercialPlan> CommercialPlans => Set<CommercialPlan>();
    public DbSet<PlanModuleEntitlement> PlanModules => Set<PlanModuleEntitlement>();
    public DbSet<PlanFeatureEntitlement> PlanFeatures => Set<PlanFeatureEntitlement>();
    public DbSet<MasterCustomerAccount> Customers => Set<MasterCustomerAccount>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<GlobalPlatformSetting> GlobalSettings => Set<GlobalPlatformSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new CommercialPlanConfiguration());
        modelBuilder.ApplyConfiguration(new PlanModuleConfiguration());
        modelBuilder.ApplyConfiguration(new PlanFeatureConfiguration());
        modelBuilder.ApplyConfiguration(new MasterCustomerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionInvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new SupportTicketConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalPlatformSettingConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.SetCreatedAudit(utcNow);
            else if (entry.State == EntityState.Modified)
                entry.Entity.SetUpdatedAudit(utcNow);
        }
    }
}

internal sealed class CommercialPlanConfiguration : IEntityTypeConfiguration<CommercialPlan>
{
    public void Configure(EntityTypeBuilder<CommercialPlan> builder)
    {
        builder.ToTable("commercial_plans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(600).IsRequired();
        builder.Property(x => x.BillingCycle).HasColumnName("billing_cycle").IsRequired();
        builder.Property(x => x.TrialDays).HasColumnName("trial_days").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.IsPublic).HasColumnName("is_public").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3).IsRequired();
        });

        ConfigureQuota(builder, x => x.EstablishmentsLimit, "establishments");
        ConfigureQuota(builder, x => x.UnitsLimit, "units");
        ConfigureQuota(builder, x => x.MarketplaceStoresLimit, "marketplace_stores");
        ConfigureQuota(builder, x => x.UsersLimit, "users");
        ConfigureQuota(builder, x => x.DevicesLimit, "devices");
        ConfigureQuota(builder, x => x.ConcurrentSessionsLimit, "concurrent_sessions");
        ConfigureQuota(builder, x => x.MonthlyOrdersLimit, "monthly_orders");

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasMany(x => x.Modules).WithOne().HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Modules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Features).WithOne().HasForeignKey(f => f.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Features).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureQuota(
        EntityTypeBuilder<CommercialPlan> builder,
        Expression<Func<CommercialPlan, QuotaLimit?>> navigation,
        string prefix)
    {
        builder.OwnsOne(navigation, quota =>
        {
            quota.Property(q => q.IsUnlimited).HasColumnName($"{prefix}_is_unlimited").IsRequired();
            quota.Property(q => q.MaxAllowed).HasColumnName($"{prefix}_max_allowed");
        });
    }
}

internal sealed class PlanModuleConfiguration : IEntityTypeConfiguration<PlanModuleEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanModuleEntitlement> builder)
    {
        builder.ToTable("plan_modules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(x => x.ModuleCode).HasColumnName("module_code").HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.ModuleCode }).IsUnique();
    }
}

internal sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeatureEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanFeatureEntitlement> builder)
    {
        builder.ToTable("plan_features");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(x => x.FeatureCode).HasColumnName("feature_code").HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.FeatureCode }).IsUnique();
    }
}

internal sealed class MasterCustomerAccountConfiguration : IEntityTypeConfiguration<MasterCustomerAccount>
{
    public void Configure(EntityTypeBuilder<MasterCustomerAccount> builder)
    {
        builder.ToTable("customer_accounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(14).IsRequired();
        builder.Property(x => x.BillingEmail).HasColumnName("billing_email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(32);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => x.DocumentNumber);
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CustomerAccountId).HasColumnName("customer_account_id").IsRequired();
        builder.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(x => x.TrialEndsAtUtc).HasColumnName("trial_ends_at_utc");
        builder.Property(x => x.CurrentPeriodStartUtc).HasColumnName("current_period_start_utc").IsRequired();
        builder.Property(x => x.CurrentPeriodEndUtc).HasColumnName("current_period_end_utc").IsRequired();
        builder.Property(x => x.SuspendedAtUtc).HasColumnName("suspended_at_utc");
        builder.Property(x => x.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(300);
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Invoices).WithOne().HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Invoices).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.CustomerAccountId, x.Status });
    }
}

internal sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        builder.Property(x => x.ReferencePeriod).HasColumnName("reference_period").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DueDateUtc).HasColumnName("due_date_utc").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
        builder.Property(x => x.PaymentGatewayReference).HasColumnName("payment_gateway_reference").HasMaxLength(128);
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(64);

        builder.OwnsOne(x => x.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
    }
}

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CustomerAccountId).HasColumnName("customer_account_id").IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.AssignedToSaasUserId).HasColumnName("assigned_to_saas_user_id");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
    }
}

internal sealed class GlobalPlatformSettingConfiguration : IEntityTypeConfiguration<GlobalPlatformSetting>
{
    public void Configure(EntityTypeBuilder<GlobalPlatformSetting> builder)
    {
        builder.ToTable("global_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(400);
        builder.Property(x => x.IsSecret).HasColumnName("is_secret").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => x.Key).IsUnique();
    }
}
