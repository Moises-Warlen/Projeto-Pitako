using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.DiningHall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfServiceQrAndTotem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "origin",
                schema: "dining",
                table: "dining_tabs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "qr_table_sessions",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_token = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_tab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_table_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "totem_order_sessions",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    totem_terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_token = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fulfillment_choice = table.Column<int>(type: "integer", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    pager_or_table_tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_choice = table.Column<int>(type: "integer", nullable: true),
                    pickup_call_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    dispatched_order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    items_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totem_order_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "totem_terminals",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    default_fulfillment_choice = table.Column<int>(type: "integer", nullable: false),
                    require_customer_name = table.Column<bool>(type: "boolean", nullable: false),
                    require_table_or_pager_number = table.Column<bool>(type: "boolean", nullable: false),
                    idle_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    last_heartbeat_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totem_terminals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waiter_calls",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dining_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dining_tab_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qr_table_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    call_type = table.Column<int>(type: "integer", nullable: false),
                    customer_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiter_calls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "totem_order_lines",
                schema: "dining",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totem_order_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acai_cup_size_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customization_summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    canonical_selections_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    customer_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totem_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_totem_order_lines_totem_order_sessions_totem_order_session_~",
                        column: x => x.totem_order_session_id,
                        principalSchema: "dining",
                        principalTable: "totem_order_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_qr_table_sessions_tenant_id_dining_table_id_status",
                schema: "dining",
                table: "qr_table_sessions",
                columns: new[] { "tenant_id", "dining_table_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_qr_table_sessions_tenant_id_session_token",
                schema: "dining",
                table: "qr_table_sessions",
                columns: new[] { "tenant_id", "session_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totem_order_lines_tenant_id_totem_order_session_id",
                schema: "dining",
                table: "totem_order_lines",
                columns: new[] { "tenant_id", "totem_order_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_totem_order_lines_totem_order_session_id",
                schema: "dining",
                table: "totem_order_lines",
                column: "totem_order_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_totem_order_sessions_tenant_id_idempotency_key",
                schema: "dining",
                table: "totem_order_sessions",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totem_order_sessions_tenant_id_session_token",
                schema: "dining",
                table: "totem_order_sessions",
                columns: new[] { "tenant_id", "session_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_totem_terminals_tenant_id_terminal_code",
                schema: "dining",
                table: "totem_terminals",
                columns: new[] { "tenant_id", "terminal_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_waiter_calls_tenant_id_status_requested_at_utc",
                schema: "dining",
                table: "waiter_calls",
                columns: new[] { "tenant_id", "status", "requested_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in SelfServiceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(DiningHallDbContext.SchemaName, table));
            }
        }

        private static readonly string[] SelfServiceTables =
        [
            "qr_table_sessions",
            "waiter_calls",
            "totem_terminals",
            "totem_order_sessions",
            "totem_order_lines"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in SelfServiceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(DiningHallDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "qr_table_sessions",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "totem_order_lines",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "totem_terminals",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "waiter_calls",
                schema: "dining");

            migrationBuilder.DropTable(
                name: "totem_order_sessions",
                schema: "dining");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "dining",
                table: "dining_tabs");
        }
    }
}
