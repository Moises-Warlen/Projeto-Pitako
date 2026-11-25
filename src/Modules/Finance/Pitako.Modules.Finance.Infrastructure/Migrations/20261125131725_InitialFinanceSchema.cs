using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Finance.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "cash_flow_ledger",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_method_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_flow_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "financial_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    dre_category = table.Column<int>(type: "integer", nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference_document = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    original_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fee_adjustment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee_adjustment_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    settled_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    due_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_method_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_configs",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    merchant_account_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    encrypted_secret_blob = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    default_mdr_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    settlement_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_gateway_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    order_or_sale_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    external_transaction_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nsu_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    card_brand = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    card_last4_digits = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    gateway_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gateway_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_batches",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exact_matches_count = table.Column<int>(type: "integer", nullable: false),
                    divergences_count = table.Column<int>(type: "integer", nullable: false),
                    total_variance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_transaction_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    internal_net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    internal_net_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    statement_net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    statement_net_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_reconciliation_lines_reconciliation_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "finance",
                        principalTable: "reconciliation_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_flow_ledger_tenant_id_occurred_at_utc",
                schema: "finance",
                table: "cash_flow_ledger",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_entries_tenant_id_entry_code",
                schema: "finance",
                table: "financial_entries",
                columns: new[] { "tenant_id", "entry_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_entries_tenant_id_kind_due_date_utc",
                schema: "finance",
                table: "financial_entries",
                columns: new[] { "tenant_id", "kind", "due_date_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_gateway_configs_tenant_id_provider_merchant_account~",
                schema: "finance",
                table: "payment_gateway_configs",
                columns: new[] { "tenant_id", "provider", "merchant_account_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_tenant_id_external_transaction_id",
                schema: "finance",
                table: "payment_transactions",
                columns: new[] { "tenant_id", "external_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_batches_tenant_id_batch_code",
                schema: "finance",
                table: "reconciliation_batches",
                columns: new[] { "tenant_id", "batch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_lines_batch_id",
                schema: "finance",
                table: "reconciliation_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_lines_tenant_id_batch_id",
                schema: "finance",
                table: "reconciliation_lines",
                columns: new[] { "tenant_id", "batch_id" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in FinanceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(FinanceDbContext.SchemaName, table));
            }
        }

        private static readonly string[] FinanceTables =
        [
            "financial_entries",
            "cash_flow_ledger",
            "payment_gateway_configs",
            "payment_transactions",
            "reconciliation_batches",
            "reconciliation_lines"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in FinanceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(FinanceDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "cash_flow_ledger",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "financial_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_gateway_configs",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "reconciliation_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "reconciliation_batches",
                schema: "finance");
        }
    }
}
