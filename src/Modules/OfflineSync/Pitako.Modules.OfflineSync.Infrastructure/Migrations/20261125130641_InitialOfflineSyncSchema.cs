using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.OfflineSync.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.OfflineSync.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialOfflineSyncSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "offline_sync");

            migrationBuilder.CreateTable(
                name: "offline_terminals",
                schema: "offline_sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    device_fingerprint_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_snapshot_version = table.Column<int>(type: "integer", nullable: false),
                    max_offline_hours = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offline_terminals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_operations",
                schema: "offline_sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offline_terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    client_sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    operation_type = table.Column<int>(type: "integer", nullable: false),
                    entity_reference_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload_json = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_snapshot_version_used = table.Column<int>(type: "integer", nullable: false),
                    client_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    server_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conflict_details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_operations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offline_terminals_tenant_id_terminal_code",
                schema: "offline_sync",
                table: "offline_terminals",
                columns: new[] { "tenant_id", "terminal_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_operations_tenant_id_offline_terminal_id_idempotency_k~",
                schema: "offline_sync",
                table: "sync_operations",
                columns: new[] { "tenant_id", "offline_terminal_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_operations_tenant_id_sync_status_client_occurred_at_utc",
                schema: "offline_sync",
                table: "sync_operations",
                columns: new[] { "tenant_id", "sync_status", "client_occurred_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OfflineTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(OfflineSyncDbContext.SchemaName, table));
            }
        }

        private static readonly string[] OfflineTables =
        [
            "offline_terminals",
            "sync_operations"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OfflineTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(OfflineSyncDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "offline_terminals",
                schema: "offline_sync");

            migrationBuilder.DropTable(
                name: "sync_operations",
                schema: "offline_sync");
        }
    }
}
