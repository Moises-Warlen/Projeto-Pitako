using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.DiningHall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDiningHallSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dining");

            migrationBuilder.CreateTable(
                name: "dining_sectors",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_sectors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dining_tables",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    current_tab_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qr_code_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dining_tabs",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: true),
                    waiter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    guest_count = table.Column<int>(type: "integer", nullable: false),
                    service_charge_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    items_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    service_charge_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    service_charge_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tabs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "table_reservations",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    party_size = table.Column<int>(type: "integer", nullable: false),
                    reserved_for_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_reservations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dining_tab_audit_events",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_tab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    details = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tab_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_dining_tab_audit_events_dining_tabs_dining_tab_id",
                        column: x => x.dining_tab_id,
                        principalSchema: "dining",
                        principalTable: "dining_tabs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dining_tab_items",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_tab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customization_summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tab_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_dining_tab_items_dining_tabs_dining_tab_id",
                        column: x => x.dining_tab_id,
                        principalSchema: "dining",
                        principalTable: "dining_tabs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dining_sectors_tenant_id_display_order",
                schema: "dining",
                table: "dining_sectors",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_dining_tab_audit_events_dining_tab_id",
                schema: "dining",
                table: "dining_tab_audit_events",
                column: "dining_tab_id");

            migrationBuilder.CreateIndex(
                name: "IX_dining_tab_audit_events_tenant_id_dining_tab_id_occurred_at~",
                schema: "dining",
                table: "dining_tab_audit_events",
                columns: new[] { "tenant_id", "dining_tab_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_dining_tab_items_dining_tab_id",
                schema: "dining",
                table: "dining_tab_items",
                column: "dining_tab_id");

            migrationBuilder.CreateIndex(
                name: "IX_dining_tab_items_tenant_id_dining_tab_id",
                schema: "dining",
                table: "dining_tab_items",
                columns: new[] { "tenant_id", "dining_tab_id" });

            migrationBuilder.CreateIndex(
                name: "IX_dining_tables_tenant_id_qr_code_token",
                schema: "dining",
                table: "dining_tables",
                columns: new[] { "tenant_id", "qr_code_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dining_tables_tenant_id_table_code",
                schema: "dining",
                table: "dining_tables",
                columns: new[] { "tenant_id", "table_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dining_tabs_tenant_id_status_opened_at_utc",
                schema: "dining",
                table: "dining_tabs",
                columns: new[] { "tenant_id", "status", "opened_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_table_reservations_tenant_id_dining_table_id_reserved_for_u~",
                schema: "dining",
                table: "table_reservations",
                columns: new[] { "tenant_id", "dining_table_id", "reserved_for_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in DiningTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(DiningHallDbContext.SchemaName, table));
            }
        }

        private static readonly string[] DiningTables =
        [
            "dining_sectors",
            "dining_tables",
            "table_reservations",
            "dining_tabs",
            "dining_tab_items",
            "dining_tab_audit_events"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in DiningTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(DiningHallDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "dining_sectors",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "dining_tab_audit_events",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "dining_tab_items",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "dining_tables",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "table_reservations",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "dining_tabs",
                schema: "dining");
        }
    }
}
