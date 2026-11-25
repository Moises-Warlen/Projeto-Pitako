using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Tenants.Infrastructure;

/// <summary>
/// Contexto de persistência do módulo de estabelecimentos, unidades e identidade visual independente.
/// </summary>
public sealed class TenantsDbContext : PitakoDbContextBase
{
    public const string SchemaName = "tenants";

    public TenantsDbContext(
        DbContextOptions<TenantsDbContext> options,
        ITenantContext tenantContext,
        IClock clock)
        : base(options, tenantContext, clock)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<StoreUnit> StoreUnits => Set<StoreUnit>();
    public DbSet<StoreVisualIdentity> StoreVisualIdentities => Set<StoreVisualIdentity>();
    public DbSet<StoreBanner> StoreBanners => Set<StoreBanner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new TenantEntityConfiguration());
        modelBuilder.ApplyConfiguration(new StoreUnitEntityConfiguration());
        modelBuilder.ApplyConfiguration(new StoreVisualIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new StoreBannerConfiguration());

        // Importante: invocar após registrar entidades para aplicar filtros globais de ITenantScopedEntity.
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class TenantEntityConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CustomerAccountId).HasColumnName("customer_account_id");
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeName).HasColumnName("trade_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SegmentCode).HasColumnName("segment_code").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(350);
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(32);
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(256);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.Property(x => x.Cnpj)
            .HasColumnName("cnpj")
            .HasMaxLength(14)
            .IsRequired()
            .HasConversion(
                cnpj => cnpj.Digits,
                raw => Cnpj.Create(raw).Value);

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.Cnpj).IsUnique();
        builder.HasIndex(x => x.CustomerAccountId);
    }
}

internal sealed class StoreUnitEntityConfiguration : IEntityTypeConfiguration<StoreUnit>
{
    public void Configure(EntityTypeBuilder<StoreUnit> builder)
    {
        builder.ToTable("store_units");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.IsHeadquarters).HasColumnName("is_headquarters").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(350);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(32);
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

internal sealed class StoreVisualIdentityConfiguration : IEntityTypeConfiguration<StoreVisualIdentity>
{
    public void Configure(EntityTypeBuilder<StoreVisualIdentity> builder)
    {
        builder.ToTable("store_visual_identities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DraftLogoUrl).HasColumnName("draft_logo_url").HasMaxLength(500);
        builder.Property(x => x.DraftCoverImageUrl).HasColumnName("draft_cover_image_url").HasMaxLength(500);
        builder.Property(x => x.DraftSlogan).HasColumnName("draft_slogan").HasMaxLength(200);
        builder.Property(x => x.DraftPrimaryColorHex).HasColumnName("draft_primary_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.DraftSecondaryColorHex).HasColumnName("draft_secondary_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.DraftAccentColorHex).HasColumnName("draft_accent_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.DraftMenuLayoutMode).HasColumnName("draft_menu_layout_mode").HasMaxLength(24).IsRequired();

        builder.Property(x => x.PublishedLogoUrl).HasColumnName("published_logo_url").HasMaxLength(500);
        builder.Property(x => x.PublishedCoverImageUrl).HasColumnName("published_cover_image_url").HasMaxLength(500);
        builder.Property(x => x.PublishedSlogan).HasColumnName("published_slogan").HasMaxLength(200);
        builder.Property(x => x.PublishedPrimaryColorHex).HasColumnName("published_primary_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.PublishedSecondaryColorHex).HasColumnName("published_secondary_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.PublishedAccentColorHex).HasColumnName("published_accent_color_hex").HasMaxLength(7).IsRequired();
        builder.Property(x => x.PublishedMenuLayoutMode).HasColumnName("published_menu_layout_mode").HasMaxLength(24).IsRequired();
        builder.Property(x => x.PublishedVersion).HasColumnName("published_version").IsRequired();
        builder.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(x => x.HasUnpublishedChanges).HasColumnName("has_unpublished_changes").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => x.TenantId).IsUnique();

        builder.HasMany(x => x.Banners)
            .WithOne()
            .HasForeignKey(b => b.VisualIdentityId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Banners).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StoreBannerConfiguration : IEntityTypeConfiguration<StoreBanner>
{
    public void Configure(EntityTypeBuilder<StoreBanner> builder)
    {
        builder.ToTable("store_banners");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.VisualIdentityId).HasColumnName("visual_identity_id").IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(500).IsRequired();
        builder.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(500);
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsDraft).HasColumnName("is_draft").IsRequired();

        builder.HasIndex(x => new { x.VisualIdentityId, x.IsDraft, x.DisplayOrder });
    }
}
