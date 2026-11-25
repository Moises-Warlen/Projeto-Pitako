using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.DiningHall.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DiningHall.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo de Salão, Mesas, Reservas e Comandas com isolamento multi-tenant.
/// </summary>
public sealed class DiningHallDbContext : PitakoDbContextBase
{
    public const string SchemaName = "dining";

    public DiningHallDbContext(
        DbContextOptions<DiningHallDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<DiningSector> Sectors => Set<DiningSector>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<TableReservation> Reservations => Set<TableReservation>();
    public DbSet<DiningTab> Tabs => Set<DiningTab>();
    public DbSet<DiningTabItem> TabItems => Set<DiningTabItem>();
    public DbSet<DiningTabAuditEvent> TabAuditEvents => Set<DiningTabAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new DiningSectorConfiguration());
        modelBuilder.ApplyConfiguration(new DiningTableConfiguration());
        modelBuilder.ApplyConfiguration(new TableReservationConfiguration());
        modelBuilder.ApplyConfiguration(new DiningTabConfiguration());
        modelBuilder.ApplyConfiguration(new DiningTabItemConfiguration());
        modelBuilder.ApplyConfiguration(new DiningTabAuditEventConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal static class DiningMoneyMapping
{
    public static void Configure<T>(OwnedNavigationBuilder<T, Money> money, string amountCol, string currencyCol) where T : class
    {
        money.Property(p => p.Amount).HasColumnName(amountCol).HasPrecision(18, 2).IsRequired();
        money.Property(p => p.Currency).HasColumnName(currencyCol).HasMaxLength(3).IsRequired();
    }
}

internal sealed class DiningSectorConfiguration : IEntityTypeConfiguration<DiningSector>
{
    public void Configure(EntityTypeBuilder<DiningSector> builder)
    {
        builder.ToTable("dining_sectors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasIndex(x => new { x.TenantId, x.DisplayOrder });
    }
}

internal sealed class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> builder)
    {
        builder.ToTable("dining_tables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SectorId).HasColumnName("sector_id").IsRequired();
        builder.Property(x => x.TableCode).HasColumnName("table_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CurrentTabId).HasColumnName("current_tab_id");
        builder.Property(x => x.QrCodeToken).HasColumnName("qr_code_token").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.TableCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.QrCodeToken }).IsUnique();
    }
}

internal sealed class TableReservationConfiguration : IEntityTypeConfiguration<TableReservation>
{
    public void Configure(EntityTypeBuilder<TableReservation> builder)
    {
        builder.ToTable("table_reservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DiningTableId).HasColumnName("dining_table_id").IsRequired();
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(40).IsRequired();
        builder.Property(x => x.PartySize).HasColumnName("party_size").IsRequired();
        builder.Property(x => x.ReservedForUtc).HasColumnName("reserved_for_utc").IsRequired();
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(300);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.DiningTableId, x.ReservedForUtc });
    }
}

internal sealed class DiningTabConfiguration : IEntityTypeConfiguration<DiningTab>
{
    public void Configure(EntityTypeBuilder<DiningTab> builder)
    {
        builder.ToTable("dining_tabs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.TabCode).HasColumnName("tab_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DiningTableId).HasColumnName("dining_table_id");
        builder.Property(x => x.WaiterUserId).HasColumnName("waiter_user_id").IsRequired();
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.GuestCount).HasColumnName("guest_count").IsRequired();
        builder.Property(x => x.ServiceChargePercent).HasColumnName("service_charge_percent").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc").IsRequired();
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.ItemsSubtotal, m => DiningMoneyMapping.Configure(m, "items_subtotal_amount", "items_subtotal_currency"));
        builder.OwnsOne(x => x.ServiceChargeAmount, m => DiningMoneyMapping.Configure(m, "service_charge_amount", "service_charge_currency"));
        builder.OwnsOne(x => x.TotalAmount, m => DiningMoneyMapping.Configure(m, "total_amount", "total_currency"));
        builder.OwnsOne(x => x.PaidAmount, m => DiningMoneyMapping.Configure(m, "paid_amount", "paid_currency"));

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.DiningTabId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.AuditEvents).WithOne().HasForeignKey(a => a.DiningTabId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.AuditEvents).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Status, x.OpenedAtUtc });
    }
}

internal sealed class DiningTabItemConfiguration : IEntityTypeConfiguration<DiningTabItem>
{
    public void Configure(EntityTypeBuilder<DiningTabItem> builder)
    {
        builder.ToTable("dining_tab_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DiningTabId).HasColumnName("dining_tab_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.ItemTitle).HasColumnName("item_title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomizationSummary).HasColumnName("customization_summary").HasMaxLength(600).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.AddedByUserId).HasColumnName("added_by_user_id");
        builder.Property(x => x.AddedAtUtc).HasColumnName("added_at_utc").IsRequired();
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(300);

        builder.OwnsOne(x => x.UnitPrice, m => DiningMoneyMapping.Configure(m, "unit_price_amount", "unit_price_currency"));
        builder.OwnsOne(x => x.LineSubtotal, m => DiningMoneyMapping.Configure(m, "line_subtotal_amount", "line_subtotal_currency"));

        builder.HasIndex(x => new { x.TenantId, x.DiningTabId });
    }
}

internal sealed class DiningTabAuditEventConfiguration : IEntityTypeConfiguration<DiningTabAuditEvent>
{
    public void Configure(EntityTypeBuilder<DiningTabAuditEvent> builder)
    {
        builder.ToTable("dining_tab_audit_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DiningTabId).HasColumnName("dining_tab_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Details).HasColumnName("details").HasMaxLength(400).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.DiningTabId, x.OccurredAtUtc });
    }
}
