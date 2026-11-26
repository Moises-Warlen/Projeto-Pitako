using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalCustomizationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allows_notes",
                schema: "catalog",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_template_key",
                schema: "catalog",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_new",
                schema: "catalog",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_recommended",
                schema: "catalog",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "preparation_minutes",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pricing_strategy",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "tags_csv",
                schema: "catalog",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "catalog",
                table: "product_variations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_flavors",
                schema: "catalog",
                table: "product_variations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "measure_amount",
                schema: "catalog",
                table: "product_variations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "measure_unit",
                schema: "catalog",
                table: "product_variations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "consumption_quantity",
                schema: "catalog",
                table: "modifier_options",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "consumption_unit_code",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "incompatible_option_ids_csv",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "loss_percent",
                schema: "catalog",
                table: "modifier_options",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "replaces_label",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supply_item_id",
                schema: "catalog",
                table: "modifier_options",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category_tag",
                schema: "catalog",
                table: "modifier_groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "counting_mode",
                schema: "catalog",
                table: "modifier_groups",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "free_allowance",
                schema: "catalog",
                table: "modifier_groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "selection_kind",
                schema: "catalog",
                table: "modifier_groups",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "rule_notes",
                schema: "catalog",
                table: "categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segment_template_code",
                schema: "catalog",
                table: "categories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "variation_modifier_rules",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_selections = table.Column<int>(type: "integer", nullable: false),
                    max_selections = table.Column<int>(type: "integer", nullable: false),
                    free_allowance = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variation_modifier_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_variation_modifier_rules_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_variation_modifier_rules_product_id",
                schema: "catalog",
                table: "variation_modifier_rules",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_variation_modifier_rules_tenant_id_product_id_product_varia~",
                schema: "catalog",
                table: "variation_modifier_rules",
                columns: new[] { "tenant_id", "product_id", "product_variation_id", "modifier_group_id" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(CatalogDbContext.SchemaName, "variation_modifier_rules"));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(CatalogDbContext.SchemaName, "variation_modifier_rules"));
            }

            migrationBuilder.DropTable(
                name: "variation_modifier_rules",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "allows_notes",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "custom_template_key",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_new",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_recommended",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "preparation_minutes",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "pricing_strategy",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tags_csv",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "catalog",
                table: "product_variations");

            migrationBuilder.DropColumn(
                name: "max_flavors",
                schema: "catalog",
                table: "product_variations");

            migrationBuilder.DropColumn(
                name: "measure_amount",
                schema: "catalog",
                table: "product_variations");

            migrationBuilder.DropColumn(
                name: "measure_unit",
                schema: "catalog",
                table: "product_variations");

            migrationBuilder.DropColumn(
                name: "consumption_quantity",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "consumption_unit_code",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "incompatible_option_ids_csv",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "loss_percent",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "replaces_label",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "supply_item_id",
                schema: "catalog",
                table: "modifier_options");

            migrationBuilder.DropColumn(
                name: "category_tag",
                schema: "catalog",
                table: "modifier_groups");

            migrationBuilder.DropColumn(
                name: "counting_mode",
                schema: "catalog",
                table: "modifier_groups");

            migrationBuilder.DropColumn(
                name: "free_allowance",
                schema: "catalog",
                table: "modifier_groups");

            migrationBuilder.DropColumn(
                name: "selection_kind",
                schema: "catalog",
                table: "modifier_groups");

            migrationBuilder.DropColumn(
                name: "rule_notes",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "segment_template_code",
                schema: "catalog",
                table: "categories");
        }
    }
}
