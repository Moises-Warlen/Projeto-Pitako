using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "combos",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    combo_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    combo_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    primary_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "modifier_groups",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    min_selections = table.Column<int>(type: "integer", nullable: false),
                    max_selections = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    base_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    sale_mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    discount_type = table.Column<int>(type: "integer", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    can_combine_with_coupons = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "combo_items",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    variation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_combo_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_combo_items_combos_combo_id",
                        column: x => x.combo_id,
                        principalSchema: "catalog",
                        principalTable: "combos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modifier_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    additional_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    additional_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    max_quantity = table.Column<int>(type: "integer", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_options", x => x.id);
                    table.ForeignKey(
                        name: "FK_modifier_options_modifier_groups_modifier_group_id",
                        column: x => x.modifier_group_id,
                        principalSchema: "catalog",
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_images",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_images_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_modifier_links",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_modifier_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_modifier_links_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variations",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variations", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_variations_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_targets",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_targets", x => x.id);
                    table.ForeignKey(
                        name: "FK_promotion_targets_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "catalog",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_tenant_id_display_order",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_combo_items_combo_id",
                schema: "catalog",
                table: "combo_items",
                column: "combo_id");

            migrationBuilder.CreateIndex(
                name: "IX_combo_items_tenant_id_combo_id",
                schema: "catalog",
                table: "combo_items",
                columns: new[] { "tenant_id", "combo_id" });

            migrationBuilder.CreateIndex(
                name: "IX_combos_tenant_id_display_order",
                schema: "catalog",
                table: "combos",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_groups_tenant_id_display_order",
                schema: "catalog",
                table: "modifier_groups",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_options_modifier_group_id",
                schema: "catalog",
                table: "modifier_options",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_modifier_options_tenant_id_modifier_group_id_display_order",
                schema: "catalog",
                table: "modifier_options",
                columns: new[] { "tenant_id", "modifier_group_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_product_images_product_id",
                schema: "catalog",
                table: "product_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_images_tenant_id_product_id_display_order",
                schema: "catalog",
                table: "product_images",
                columns: new[] { "tenant_id", "product_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_product_modifier_links_product_id",
                schema: "catalog",
                table: "product_modifier_links",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_modifier_links_tenant_id_product_id_modifier_group_~",
                schema: "catalog",
                table: "product_modifier_links",
                columns: new[] { "tenant_id", "product_id", "modifier_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variations_product_id",
                schema: "catalog",
                table: "product_variations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variations_tenant_id_product_id_display_order",
                schema: "catalog",
                table: "product_variations",
                columns: new[] { "tenant_id", "product_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_products_tenant_id_category_id_display_order",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "category_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_targets_promotion_id",
                schema: "catalog",
                table: "promotion_targets",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_targets_tenant_id_promotion_id_target_type_target~",
                schema: "catalog",
                table: "promotion_targets",
                columns: new[] { "tenant_id", "promotion_id", "target_type", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotions_tenant_id_is_active",
                schema: "catalog",
                table: "promotions",
                columns: new[] { "tenant_id", "is_active" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CatalogTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(CatalogDbContext.SchemaName, table));
            }
        }

        private static readonly string[] CatalogTables =
        [
            "categories", "products", "product_images", "product_variations", "product_modifier_links",
            "modifier_groups", "modifier_options", "combos", "combo_items", "promotions", "promotion_targets"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CatalogTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(CatalogDbContext.SchemaName, table));
            }

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "combo_items",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "modifier_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_images",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_modifier_links",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variations",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "promotion_targets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "combos",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "modifier_groups",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "catalog");
        }
    }
}
