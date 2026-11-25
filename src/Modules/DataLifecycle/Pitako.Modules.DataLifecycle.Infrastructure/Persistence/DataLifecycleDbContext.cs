using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.DataLifecycle.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do ciclo de vida de dados (migrações, backups cifrados e solicitações LGPD).
/// </summary>
public sealed class DataLifecycleDbContext : PitakoDbContextBase
{
    public const string SchemaName = "data_lifecycle";

    public DataLifecycleDbContext(
        DbContextOptions<DataLifecycleDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<DataMigrationJob> MigrationJobs => Set<DataMigrationJob>();
    public DbSet<TenantBackupSnapshot> BackupSnapshots => Set<TenantBackupSnapshot>();
    public DbSet<LgpdDataRequest> LgpdRequests => Set<LgpdDataRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new DataMigrationJobConfiguration());
        modelBuilder.ApplyConfiguration(new TenantBackupSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new LgpdDataRequestConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class DataMigrationJobConfiguration : IEntityTypeConfiguration<DataMigrationJob>
{
    public void Configure(EntityTypeBuilder<DataMigrationJob> builder)
    {
        builder.ToTable("migration_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SourceSystem).HasColumnName("source_system").IsRequired();
        builder.Property(x => x.IsDryRun).HasColumnName("is_dry_run").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.TotalRows).HasColumnName("total_rows").IsRequired();
        builder.Property(x => x.ImportedCount).HasColumnName("imported_count").IsRequired();
        builder.Property(x => x.SkippedDuplicateCount).HasColumnName("skipped_duplicate_count").IsRequired();
        builder.Property(x => x.FailedCount).HasColumnName("failed_count").IsRequired();
        builder.Property(x => x.NormalizedItemsJson).HasColumnName("normalized_items_json").HasMaxLength(32000).IsRequired();
        builder.Property(x => x.ValidationErrorsJson).HasColumnName("validation_errors_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.ExecutedAtUtc).HasColumnName("executed_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.SourceSystem, x.ExecutedAtUtc });
    }
}

internal sealed class TenantBackupSnapshotConfiguration : IEntityTypeConfiguration<TenantBackupSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantBackupSnapshot> builder)
    {
        builder.ToTable("tenant_backup_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.BackupCode).HasColumnName("backup_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EncryptedBundleBase64).HasColumnName("encrypted_bundle_base64").HasMaxLength(64000).IsRequired();
        builder.Property(x => x.PlainBundleSha256).HasColumnName("plain_bundle_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.IncludesCatalog).HasColumnName("includes_catalog").IsRequired();
        builder.Property(x => x.IncludesOrders).HasColumnName("includes_orders").IsRequired();
        builder.Property(x => x.IncludesCustomers).HasColumnName("includes_customers").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.BackupCreatedAtUtc).HasColumnName("backup_created_at_utc").IsRequired();
        builder.Property(x => x.RestoredByUserId).HasColumnName("restored_by_user_id");
        builder.Property(x => x.RestoredAtUtc).HasColumnName("restored_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.BackupCode }).IsUnique();
    }
}

internal sealed class LgpdDataRequestConfiguration : IEntityTypeConfiguration<LgpdDataRequest>
{
    public void Configure(EntityTypeBuilder<LgpdDataRequest> builder)
    {
        builder.ToTable("lgpd_data_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.RequestType).HasColumnName("request_type").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.SubjectHash).HasColumnName("subject_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SubjectMasked).HasColumnName("subject_masked").HasMaxLength(80).IsRequired();
        builder.Property(x => x.ReasonOrNotes).HasColumnName("reason_or_notes").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ExportedPortabilityJson).HasColumnName("exported_portability_json").HasMaxLength(32000);
        builder.Property(x => x.AnonymizedRecordsCount).HasColumnName("anonymized_records_count").IsRequired();
        builder.Property(x => x.ProcessedByUserId).HasColumnName("processed_by_user_id").IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.SubjectHash, x.ProcessedAtUtc });
    }
}
