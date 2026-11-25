using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Orders.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialShoppingCartSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "shopping_carts",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_session_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    customer_identifier = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    applied_coupon_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    delivery_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    items_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    items_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    delivery_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_carts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopping_cart_items",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shopping_cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_kind = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acai_cup_size_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weighing_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customization_summary_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    selections_payload_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    customization_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    has_non_combinable_promotion = table.Column<bool>(type: "boolean", nullable: false),
                    customer_notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_cart_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_shopping_cart_items_shopping_carts_shopping_cart_id",
                        column: x => x.shopping_cart_id,
                        principalSchema: "orders",
                        principalTable: "shopping_carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_cart_items_shopping_cart_id",
                schema: "orders",
                table: "shopping_cart_items",
                column: "shopping_cart_id");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_cart_items_tenant_id_shopping_cart_id_customizatio~",
                schema: "orders",
                table: "shopping_cart_items",
                columns: new[] { "tenant_id", "shopping_cart_id", "customization_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_carts_tenant_id_cart_session_key",
                schema: "orders",
                table: "shopping_carts",
                columns: new[] { "tenant_id", "cart_session_key" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OrdersTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(OrdersDbContext.SchemaName, table));
            }
        }

        private static readonly string[] OrdersTables =
        [
            "shopping_carts",
            "shopping_cart_items"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in OrdersTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(OrdersDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "shopping_cart_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shopping_carts",
                schema: "orders");
        }
    }
}
