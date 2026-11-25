using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Tenants.Infrastructure;

/// <summary>
/// Contexto de persistência do módulo de estabelecimentos e unidades com isolamento multi-tenant.
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new TenantEntityConfiguration());
        modelBuilder.ApplyConfiguration(new StoreUnitEntityConfiguration());

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
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeName).HasColumnName("trade_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SegmentCode).HasColumnName("segment_code").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
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
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
