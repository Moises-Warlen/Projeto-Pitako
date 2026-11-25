using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockPurchasesAndProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_orders",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_sheet_name_snapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    technical_sheet_version = table.Column<int>(type: "integer", nullable: false),
                    output_supply_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_multiplier = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    produced_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    yield_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    batch_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_production_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_production_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    executed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    minimum_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allow_negative_stock = table.Column<bool>(type: "boolean", nullable: false),
                    auto_deduct_on_order = table.Column<bool>(type: "boolean", nullable: false),
                    block_expired_lots = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_consumptions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_name_snapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    quantity_consumed_in_stock_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    stock_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    calculated_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_consumptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_production_consumptions_production_orders_production_order_~",
                        column: x => x.production_order_id,
                        principalSchema: "inventory",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipt_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_name_snapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    purchased_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    purchase_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    credited_stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    stock_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    batch_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    manufactured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipt_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_items_purchase_receipts_purchase_receipt_id",
                        column: x => x.purchase_receipt_id,
                        principalSchema: "inventory",
                        principalTable: "purchase_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_lots",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    initial_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    remaining_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    manufactured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_lots", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_lots_stock_balances_stock_balance_id",
                        column: x => x.stock_balance_id,
                        principalSchema: "inventory",
                        principalTable: "stock_balances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_type = table.Column<int>(type: "integer", nullable: false),
                    quantity_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    previous_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    resulting_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_balances_stock_balance_id",
                        column: x => x.stock_balance_id,
                        principalSchema: "inventory",
                        principalTable: "stock_balances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_production_consumptions_production_order_id",
                schema: "inventory",
                table: "production_consumptions",
                column: "production_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_tenant_id_technical_sheet_id_executed_at_~",
                schema: "inventory",
                table: "production_orders",
                columns: new[] { "tenant_id", "technical_sheet_id", "executed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_items_purchase_receipt_id",
                schema: "inventory",
                table: "purchase_receipt_items",
                column: "purchase_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_tenant_id_idempotency_key",
                schema: "inventory",
                table: "purchase_receipts",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_tenant_id_received_at_utc",
                schema: "inventory",
                table: "purchase_receipts",
                columns: new[] { "tenant_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_tenant_id_supply_item_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "supply_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_lots_stock_balance_id",
                schema: "inventory",
                table: "stock_lots",
                column: "stock_balance_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_lots_tenant_id_supply_item_id_expires_at_utc",
                schema: "inventory",
                table: "stock_lots",
                columns: new[] { "tenant_id", "supply_item_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_stock_balance_id",
                schema: "inventory",
                table: "stock_movements",
                column: "stock_balance_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_idempotency_key",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_reference_type_reference_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_tenant_id_supply_item_id_occurred_at_utc",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "supply_item_id", "occurred_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in StockTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(InventoryDbContext.SchemaName, table));
            }
        }

        private static readonly string[] StockTables =
        [
            "stock_balances",
            "stock_lots",
            "stock_movements",
            "purchase_receipts",
            "purchase_receipt_items",
            "production_orders",
            "production_consumptions"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in StockTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(InventoryDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "production_consumptions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "purchase_receipt_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_lots",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "production_orders",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "purchase_receipts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");
        }
    }
}
