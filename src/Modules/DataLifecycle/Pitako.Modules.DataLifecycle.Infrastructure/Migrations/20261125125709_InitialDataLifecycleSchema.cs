using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.DataLifecycle.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.DataLifecycle.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDataLifecycleSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "data_lifecycle");

            migrationBuilder.CreateTable(
                name: "lgpd_data_requests",
                schema: "data_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    subject_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_masked = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason_or_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    exported_portability_json = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: true),
                    anonymized_records_count = table.Column<int>(type: "integer", nullable: false),
                    processed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lgpd_data_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "migration_jobs",
                schema: "data_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<int>(type: "integer", nullable: false),
                    is_dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    imported_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    normalized_items_json = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    validation_errors_json = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_backup_snapshots",
                schema: "data_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    backup_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    encrypted_bundle_base64 = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    plain_bundle_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    includes_catalog = table.Column<bool>(type: "boolean", nullable: false),
                    includes_orders = table.Column<bool>(type: "boolean", nullable: false),
                    includes_customers = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    backup_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    restored_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    restored_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_backup_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lgpd_data_requests_tenant_id_subject_hash_processed_at_utc",
                schema: "data_lifecycle",
                table: "lgpd_data_requests",
                columns: new[] { "tenant_id", "subject_hash", "processed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_migration_jobs_tenant_id_source_system_executed_at_utc",
                schema: "data_lifecycle",
                table: "migration_jobs",
                columns: new[] { "tenant_id", "source_system", "executed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_backup_snapshots_tenant_id_backup_code",
                schema: "data_lifecycle",
                table: "tenant_backup_snapshots",
                columns: new[] { "tenant_id", "backup_code" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in LifecycleTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(DataLifecycleDbContext.SchemaName, table));
            }
        }

        private static readonly string[] LifecycleTables =
        [
            "migration_jobs",
            "tenant_backup_snapshots",
            "lgpd_data_requests"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in LifecycleTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(DataLifecycleDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "lgpd_data_requests",
                schema: "data_lifecycle");

            migrationBuilder.DropTable(
                name: "migration_jobs",
                schema: "data_lifecycle");

            migrationBuilder.DropTable(
                name: "tenant_backup_snapshots",
                schema: "data_lifecycle");
        }
    }
}
