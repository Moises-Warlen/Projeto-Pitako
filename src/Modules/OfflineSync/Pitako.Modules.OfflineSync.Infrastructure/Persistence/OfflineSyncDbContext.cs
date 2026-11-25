using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.OfflineSync.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.OfflineSync.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do modo offline e fila de sincronização idempotente com isolamento multi-tenant.
/// </summary>
public sealed class OfflineSyncDbContext : PitakoDbContextBase
{
    public const string SchemaName = "offline_sync";

    public OfflineSyncDbContext(
        DbContextOptions<OfflineSyncDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<OfflineTerminalRegistration> Terminals => Set<OfflineTerminalRegistration>();
    public DbSet<OfflineSyncOperation> SyncOperations => Set<OfflineSyncOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new OfflineTerminalRegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new OfflineSyncOperationConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class OfflineTerminalRegistrationConfiguration : IEntityTypeConfiguration<OfflineTerminalRegistration>
{
    public void Configure(EntityTypeBuilder<OfflineTerminalRegistration> builder)
    {
        builder.ToTable("offline_terminals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.TerminalCode).HasColumnName("terminal_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DeviceFingerprintHash).HasColumnName("device_fingerprint_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CatalogSnapshotVersion).HasColumnName("catalog_snapshot_version").IsRequired();
        builder.Property(x => x.MaxOfflineHours).HasColumnName("max_offline_hours").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.LastSyncUtc).HasColumnName("last_sync_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.TerminalCode }).IsUnique();
    }
}

internal sealed class OfflineSyncOperationConfiguration : IEntityTypeConfiguration<OfflineSyncOperation>
{
    public void Configure(EntityTypeBuilder<OfflineSyncOperation> builder)
    {
        builder.ToTable("sync_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OfflineTerminalId).HasColumnName("offline_terminal_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ClientSequenceNumber).HasColumnName("client_sequence_number").IsRequired();
        builder.Property(x => x.OperationType).HasColumnName("operation_type").IsRequired();
        builder.Property(x => x.EntityReferenceKey).HasColumnName("entity_reference_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.PayloadSha256).HasColumnName("payload_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CatalogSnapshotVersionUsed).HasColumnName("catalog_snapshot_version_used").IsRequired();
        builder.Property(x => x.ClientOccurredAtUtc).HasColumnName("client_occurred_at_utc").IsRequired();
        builder.Property(x => x.SyncStatus).HasColumnName("sync_status").IsRequired();
        builder.Property(x => x.ServerEntityId).HasColumnName("server_entity_id");
        builder.Property(x => x.ConflictDetails).HasColumnName("conflict_details").HasMaxLength(500);
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(300);
        builder.Property(x => x.SyncedAtUtc).HasColumnName("synced_at_utc");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.OfflineTerminalId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SyncStatus, x.ClientOccurredAtUtc });
    }
}
