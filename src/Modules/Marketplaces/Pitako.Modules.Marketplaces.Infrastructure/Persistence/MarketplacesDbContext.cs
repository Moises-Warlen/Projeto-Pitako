using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Marketplaces.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do Hub de Marketplaces (contas cifradas, lojas vinculadas, webhooks idempotentes e SKUs).
/// </summary>
public sealed class MarketplacesDbContext : PitakoDbContextBase
{
    public const string SchemaName = "marketplaces";

    public MarketplacesDbContext(
        DbContextOptions<MarketplacesDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<MarketplacePartnerAccount> PartnerAccounts => Set<MarketplacePartnerAccount>();
    public DbSet<MarketplaceStoreBinding> StoreBindings => Set<MarketplaceStoreBinding>();
    public DbSet<MarketplaceInboundEvent> InboundEvents => Set<MarketplaceInboundEvent>();
    public DbSet<MarketplaceCatalogMapping> CatalogMappings => Set<MarketplaceCatalogMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new MarketplacePartnerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new MarketplaceStoreBindingConfiguration());
        modelBuilder.ApplyConfiguration(new MarketplaceInboundEventConfiguration());
        modelBuilder.ApplyConfiguration(new MarketplaceCatalogMappingConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class MarketplacePartnerAccountConfiguration : IEntityTypeConfiguration<MarketplacePartnerAccount>
{
    public void Configure(EntityTypeBuilder<MarketplacePartnerAccount> builder)
    {
        builder.ToTable("partner_accounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.AuthKind).HasColumnName("auth_kind").IsRequired();
        builder.Property(x => x.AccountLabel).HasColumnName("account_label").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ClientOrMerchantIdentifier).HasColumnName("client_or_merchant_identifier").HasMaxLength(160).IsRequired();
        builder.Property(x => x.EncryptedCredentialsBlob).HasColumnName("encrypted_credentials_blob").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.LastHealthCheckUtc).HasColumnName("last_health_check_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.ClientOrMerchantIdentifier });
    }
}

internal sealed class MarketplaceStoreBindingConfiguration : IEntityTypeConfiguration<MarketplaceStoreBinding>
{
    public void Configure(EntityTypeBuilder<MarketplaceStoreBinding> builder)
    {
        builder.ToTable("store_bindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.PartnerAccountId).HasColumnName("partner_account_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.ExternalMerchantId).HasColumnName("external_merchant_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ExternalStoreName).HasColumnName("external_store_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.AutoAcceptOrders).HasColumnName("auto_accept_orders").IsRequired();
        builder.Property(x => x.LastSyncUtc).HasColumnName("last_sync_utc");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.ExternalMerchantId }).IsUnique();
    }
}

internal sealed class MarketplaceInboundEventConfiguration : IEntityTypeConfiguration<MarketplaceInboundEvent>
{
    public void Configure(EntityTypeBuilder<MarketplaceInboundEvent> builder)
    {
        builder.ToTable("inbound_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.StoreBindingId).HasColumnName("store_binding_id");
        builder.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ExternalMerchantId).HasColumnName("external_merchant_id").HasMaxLength(120).IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        builder.Property(x => x.RawPayloadJson).HasColumnName("raw_payload_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.SignatureVerified).HasColumnName("signature_verified").IsRequired();
        builder.Property(x => x.SyncStatus).HasColumnName("sync_status").IsRequired();
        builder.Property(x => x.NormalizedCustomerName).HasColumnName("normalized_customer_name").HasMaxLength(160);
        builder.Property(x => x.NormalizedFulfillment).HasColumnName("normalized_fulfillment");
        builder.Property(x => x.NormalizedItemsJson).HasColumnName("normalized_items_json").HasMaxLength(12000).IsRequired();
        builder.Property(x => x.InternalOrderId).HasColumnName("internal_order_id");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.NormalizedTotal, m =>
        {
            m.Property(p => p.Amount).HasColumnName("normalized_total_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("normalized_total_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.ExternalEventId }).IsUnique();
    }
}

internal sealed class MarketplaceCatalogMappingConfiguration : IEntityTypeConfiguration<MarketplaceCatalogMapping>
{
    public void Configure(EntityTypeBuilder<MarketplaceCatalogMapping> builder)
    {
        builder.ToTable("catalog_mappings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreBindingId).HasColumnName("store_binding_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.InternalProductId).HasColumnName("internal_product_id").IsRequired();
        builder.Property(x => x.InternalItemTitle).HasColumnName("internal_item_title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExternalSkuCode).HasColumnName("external_sku_code").HasMaxLength(120).IsRequired();
        builder.Property(x => x.IsPausedAtPartner).HasColumnName("is_paused_at_partner").IsRequired();
        builder.Property(x => x.LastSyncedAtUtc).HasColumnName("last_synced_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.OwnsOne(x => x.LastSyncedPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("last_synced_price_amount").HasPrecision(18, 2).IsRequired();
            m.Property(p => p.Currency).HasColumnName("last_synced_price_currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(x => new { x.TenantId, x.StoreBindingId, x.InternalProductId }).IsUnique();
    }
}
