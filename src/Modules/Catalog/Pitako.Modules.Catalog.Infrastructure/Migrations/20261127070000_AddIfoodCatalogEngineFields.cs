using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitako.Modules.Catalog.Infrastructure.Migrations
{
    public partial class AddIfoodCatalogEngineFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "template_kind",
                schema: "catalog",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "availability_schedule_json",
                schema: "catalog",
                table: "categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "promotional_price_amount",
                schema: "catalog",
                table: "products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "promotional_price_currency",
                schema: "catalog",
                table: "products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "track_inventory",
                schema: "catalog",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "stock_quantity",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "auto_pause_on_zero_stock",
                schema: "catalog",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "serves_people_count",
                schema: "catalog",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dietary_tags_csv",
                schema: "catalog",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "availability_schedule_json",
                schema: "catalog",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "track_inventory",
                schema: "catalog",
                table: "product_variations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "stock_quantity",
                schema: "catalog",
                table: "product_variations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sku",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "template_kind", schema: "catalog", table: "categories");
            migrationBuilder.DropColumn(name: "availability_schedule_json", schema: "catalog", table: "categories");
            migrationBuilder.DropColumn(name: "promotional_price_amount", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "promotional_price_currency", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "track_inventory", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "stock_quantity", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "auto_pause_on_zero_stock", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "serves_people_count", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "dietary_tags_csv", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "availability_schedule_json", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "track_inventory", schema: "catalog", table: "product_variations");
            migrationBuilder.DropColumn(name: "stock_quantity", schema: "catalog", table: "product_variations");
            migrationBuilder.DropColumn(name: "image_url", schema: "catalog", table: "modifier_options");
            migrationBuilder.DropColumn(name: "sku", schema: "catalog", table: "modifier_options");
        }
    }
}
