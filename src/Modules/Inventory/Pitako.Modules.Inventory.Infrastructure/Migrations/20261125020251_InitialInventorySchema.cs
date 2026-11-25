using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "custom_units",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    factor_to_base_unit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supply_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    purchase_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purchase_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    content_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_quantity_per_package = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    purchase_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    purchase_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "technical_sheets",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    yield_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    yield_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ingredients_total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    packagings_total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_per_portion = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_sheets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supply_price_histories",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    new_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_price_histories", x => x.id);
                    table.ForeignKey(
                        name: "FK_supply_price_histories_supply_items_supply_item_id",
                        column: x => x.supply_item_id,
                        principalSchema: "inventory",
                        principalTable: "supply_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technical_sheet_cost_snapshots",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    ingredients_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    packagings_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    yield_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_per_portion = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    trigger_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_sheet_cost_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_technical_sheet_cost_snapshots_technical_sheets_technical_s~",
                        column: x => x.technical_sheet_id,
                        principalSchema: "inventory",
                        principalTable: "technical_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technical_sheet_ingredients",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_name_snapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    calculated_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_sheet_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "FK_technical_sheet_ingredients_technical_sheets_technical_shee~",
                        column: x => x.technical_sheet_id,
                        principalSchema: "inventory",
                        principalTable: "technical_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technical_sheet_packagings",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    calculated_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_sheet_packagings", x => x.id);
                    table.ForeignKey(
                        name: "FK_technical_sheet_packagings_technical_sheets_technical_sheet~",
                        column: x => x.technical_sheet_id,
                        principalSchema: "inventory",
                        principalTable: "technical_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_units_tenant_id_code",
                schema: "inventory",
                table: "custom_units",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supply_items_tenant_id_name",
                schema: "inventory",
                table: "supply_items",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_supply_price_histories_supply_item_id",
                schema: "inventory",
                table: "supply_price_histories",
                column: "supply_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_supply_price_histories_tenant_id_supply_item_id",
                schema: "inventory",
                table: "supply_price_histories",
                columns: new[] { "tenant_id", "supply_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheet_cost_snapshots_technical_sheet_id",
                schema: "inventory",
                table: "technical_sheet_cost_snapshots",
                column: "technical_sheet_id");

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheet_cost_snapshots_tenant_id_technical_sheet_id~",
                schema: "inventory",
                table: "technical_sheet_cost_snapshots",
                columns: new[] { "tenant_id", "technical_sheet_id", "version" });

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheet_ingredients_technical_sheet_id",
                schema: "inventory",
                table: "technical_sheet_ingredients",
                column: "technical_sheet_id");

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheet_ingredients_tenant_id_supply_item_id",
                schema: "inventory",
                table: "technical_sheet_ingredients",
                columns: new[] { "tenant_id", "supply_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheet_packagings_technical_sheet_id",
                schema: "inventory",
                table: "technical_sheet_packagings",
                column: "technical_sheet_id");

            migrationBuilder.CreateIndex(
                name: "IX_technical_sheets_tenant_id_product_id",
                schema: "inventory",
                table: "technical_sheets",
                columns: new[] { "tenant_id", "product_id" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in InventoryTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(InventoryDbContext.SchemaName, table));
            }
        }

        private static readonly string[] InventoryTables =
        [
            "custom_units",
            "supply_items",
            "supply_price_histories",
            "technical_sheets",
            "technical_sheet_ingredients",
            "technical_sheet_packagings",
            "technical_sheet_cost_snapshots"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in InventoryTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(InventoryDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "custom_units",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "supply_price_histories",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "technical_sheet_cost_snapshots",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "technical_sheet_ingredients",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "technical_sheet_packagings",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "supply_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "technical_sheets",
                schema: "inventory");
        }
    }
}
