using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Marketplaces.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Marketplaces.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceBulkJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_jobs",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<int>(type: "integer", nullable: false),
                    operation_summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    internal_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    target_paused_state = table.Column<bool>(type: "boolean", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_targets = table.Column<int>(type: "integer", nullable: false),
                    succeeded_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bulk_job_items",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulk_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    external_merchant_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_store_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    target_sku_or_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_job_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_job_items_bulk_jobs_bulk_job_id",
                        column: x => x.bulk_job_id,
                        principalSchema: "marketplaces",
                        principalTable: "bulk_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bulk_job_items_bulk_job_id",
                schema: "marketplaces",
                table: "bulk_job_items",
                column: "bulk_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_bulk_job_items_tenant_id_bulk_job_id",
                schema: "marketplaces",
                table: "bulk_job_items",
                columns: new[] { "tenant_id", "bulk_job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bulk_jobs_tenant_id_status_started_at_utc",
                schema: "marketplaces",
                table: "bulk_jobs",
                columns: new[] { "tenant_id", "status", "started_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in BulkTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(MarketplacesDbContext.SchemaName, table));
            }
        }

        private static readonly string[] BulkTables =
        [
            "bulk_jobs",
            "bulk_job_items"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in BulkTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(MarketplacesDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "bulk_job_items",
                schema: "marketplaces");

            migrationBuilder.DropTable(
                name: "bulk_jobs",
                schema: "marketplaces");
        }
    }
}
