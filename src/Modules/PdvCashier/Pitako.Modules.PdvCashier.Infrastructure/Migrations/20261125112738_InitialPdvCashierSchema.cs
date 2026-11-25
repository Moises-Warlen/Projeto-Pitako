using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.PdvCashier.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.PdvCashier.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPdvCashierSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pdv");

            migrationBuilder.CreateTable(
                name: "cash_register_sessions",
                schema: "pdv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opening_balance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    opening_balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    expected_cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_cash_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    counted_cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    counted_cash_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    cash_variance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cash_variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_register_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pdv_sales",
                schema: "pdv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    discount_authorized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pdv_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "pdv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    pdv_sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_cash_movements_cash_register_sessions_cash_register_session~",
                        column: x => x.cash_register_session_id,
                        principalSchema: "pdv",
                        principalTable: "cash_register_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pdv_sale_payments",
                schema: "pdv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pdv_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    allocated_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tendered_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tendered_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    change_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    change_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    authorization_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pdv_sale_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_pdv_sale_payments_pdv_sales_pdv_sale_id",
                        column: x => x.pdv_sale_id,
                        principalSchema: "pdv",
                        principalTable: "pdv_sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_cash_register_session_id",
                schema: "pdv",
                table: "cash_movements",
                column: "cash_register_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_tenant_id_cash_register_session_id_occurred_~",
                schema: "pdv",
                table: "cash_movements",
                columns: new[] { "tenant_id", "cash_register_session_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_register_sessions_tenant_id_terminal_code_status",
                schema: "pdv",
                table: "cash_register_sessions",
                columns: new[] { "tenant_id", "terminal_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_pdv_sale_payments_pdv_sale_id",
                schema: "pdv",
                table: "pdv_sale_payments",
                column: "pdv_sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_pdv_sale_payments_tenant_id_pdv_sale_id",
                schema: "pdv",
                table: "pdv_sale_payments",
                columns: new[] { "tenant_id", "pdv_sale_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pdv_sales_tenant_id_cash_register_session_id_settled_at_utc",
                schema: "pdv",
                table: "pdv_sales",
                columns: new[] { "tenant_id", "cash_register_session_id", "settled_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_pdv_sales_tenant_id_idempotency_key",
                schema: "pdv",
                table: "pdv_sales",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in PdvTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(PdvCashierDbContext.SchemaName, table));
            }
        }

        private static readonly string[] PdvTables =
        [
            "cash_register_sessions",
            "cash_movements",
            "pdv_sales",
            "pdv_sale_payments"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in PdvTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(PdvCashierDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "pdv");

            migrationBuilder.DropTable(
                name: "pdv_sale_payments",
                schema: "pdv");

            migrationBuilder.DropTable(
                name: "cash_register_sessions",
                schema: "pdv");

            migrationBuilder.DropTable(
                name: "pdv_sales",
                schema: "pdv");
        }
    }
}
