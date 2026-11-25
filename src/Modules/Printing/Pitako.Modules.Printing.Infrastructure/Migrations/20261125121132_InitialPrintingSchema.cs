using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Printing.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Printing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrintingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "printing");

            migrationBuilder.CreateTable(
                name: "print_jobs",
                schema: "printing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sector_type = table.Column<int>(type: "integer", nullable: false),
                    document_kind = table.Column<int>(type: "integer", nullable: false),
                    order_or_tab_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_printer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_printer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    used_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    fallback_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    store_trade_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    store_cnpj = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    store_logo_ref = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    rendered_plain_text = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    esc_pos_payload_base64 = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    copies_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    job_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    printed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "printer_profiles",
                schema: "printing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    printer_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    connection_kind = table.Column<int>(type: "integer", nullable: false),
                    endpoint_or_port = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    paper_width_mm = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    backup_printer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supports_logo_raster = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printer_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sector_routing_rules",
                schema: "printing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sector_type = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_printer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fallback_printer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    copies_count = table.Column<int>(type: "integer", nullable: false),
                    auto_print_on_order_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sector_routing_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_tenant_id_status_job_created_at_utc",
                schema: "printing",
                table: "print_jobs",
                columns: new[] { "tenant_id", "status", "job_created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_printer_profiles_tenant_id_printer_code",
                schema: "printing",
                table: "printer_profiles",
                columns: new[] { "tenant_id", "printer_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sector_routing_rules_tenant_id_sector_type_category_id",
                schema: "printing",
                table: "sector_routing_rules",
                columns: new[] { "tenant_id", "sector_type", "category_id" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in PrintingTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(PrintingDbContext.SchemaName, table));
            }
        }

        private static readonly string[] PrintingTables =
        [
            "printer_profiles",
            "sector_routing_rules",
            "print_jobs"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in PrintingTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(PrintingDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "print_jobs",
                schema: "printing");

            migrationBuilder.DropTable(
                name: "printer_profiles",
                schema: "printing");

            migrationBuilder.DropTable(
                name: "sector_routing_rules",
                schema: "printing");
        }
    }
}
