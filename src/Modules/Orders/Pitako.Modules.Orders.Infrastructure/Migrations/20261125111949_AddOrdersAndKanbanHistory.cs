using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Orders.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersAndKanbanHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    fulfillment_mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    customer_identifier = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    delivery_address_summary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    applied_coupon_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    items_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    items_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    delivery_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    delivery_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_delivery_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_delivery_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    placed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_kind = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acai_cup_size_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weighing_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customization_summary_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    selections_payload_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    customer_notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_status_histories",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_histories", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_status_histories_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                schema: "orders",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_tenant_id_product_id",
                schema: "orders",
                table: "order_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_status_histories_order_id",
                schema: "orders",
                table: "order_status_histories",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_status_histories_tenant_id_order_id_changed_at_utc",
                schema: "orders",
                table: "order_status_histories",
                columns: new[] { "tenant_id", "order_id", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_idempotency_key",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_status_placed_at_utc",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "status", "placed_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OrderTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(OrdersDbContext.SchemaName, table));
            }
        }

        private static readonly string[] OrderTables =
        [
            "orders",
            "order_items",
            "order_status_histories"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OrderTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(OrdersDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "order_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_status_histories",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}
