using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;

#nullable disable

namespace Pitako.Modules.AcaiBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcaiCategoriesCostAndCupRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "addon_categories",
                schema: "acai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    is_free_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addon_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_addon_categories_tenant_id_display_order",
                schema: "acai",
                table: "addon_categories",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.AddColumn<string>(name: "description", schema: "acai", table: "cup_sizes", type: "character varying(400)", maxLength: 400, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "cost_price_amount", schema: "acai", table: "cup_sizes", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(name: "cost_price_currency", schema: "acai", table: "cup_sizes", type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "BRL");
            migrationBuilder.AddColumn<bool>(name: "allow_free_toppings", schema: "acai", table: "cup_sizes", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "allow_paid_addons", schema: "acai", table: "cup_sizes", type: "boolean", nullable: false, defaultValue: true);

            migrationBuilder.AddColumn<string>(name: "description", schema: "acai", table: "free_toppings", type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "addon_category_id", schema: "acai", table: "free_toppings", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "category_name", schema: "acai", table: "free_toppings", type: "character varying(140)", maxLength: 140, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "cost_price_amount", schema: "acai", table: "free_toppings", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(name: "cost_price_currency", schema: "acai", table: "free_toppings", type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "BRL");

            migrationBuilder.AddColumn<string>(name: "description", schema: "acai", table: "paid_addons", type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "addon_category_id", schema: "acai", table: "paid_addons", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "category_name", schema: "acai", table: "paid_addons", type: "character varying(140)", maxLength: 140, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "cost_price_amount", schema: "acai", table: "paid_addons", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(name: "cost_price_currency", schema: "acai", table: "paid_addons", type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "BRL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "addon_categories", schema: "acai");
            migrationBuilder.DropColumn(name: "description", schema: "acai", table: "cup_sizes");
            migrationBuilder.DropColumn(name: "cost_price_amount", schema: "acai", table: "cup_sizes");
            migrationBuilder.DropColumn(name: "cost_price_currency", schema: "acai", table: "cup_sizes");
            migrationBuilder.DropColumn(name: "allow_free_toppings", schema: "acai", table: "cup_sizes");
            migrationBuilder.DropColumn(name: "allow_paid_addons", schema: "acai", table: "cup_sizes");
            migrationBuilder.DropColumn(name: "description", schema: "acai", table: "free_toppings");
            migrationBuilder.DropColumn(name: "addon_category_id", schema: "acai", table: "free_toppings");
            migrationBuilder.DropColumn(name: "category_name", schema: "acai", table: "free_toppings");
            migrationBuilder.DropColumn(name: "cost_price_amount", schema: "acai", table: "free_toppings");
            migrationBuilder.DropColumn(name: "cost_price_currency", schema: "acai", table: "free_toppings");
            migrationBuilder.DropColumn(name: "description", schema: "acai", table: "paid_addons");
            migrationBuilder.DropColumn(name: "addon_category_id", schema: "acai", table: "paid_addons");
            migrationBuilder.DropColumn(name: "category_name", schema: "acai", table: "paid_addons");
            migrationBuilder.DropColumn(name: "cost_price_amount", schema: "acai", table: "paid_addons");
            migrationBuilder.DropColumn(name: "cost_price_currency", schema: "acai", table: "paid_addons");
        }
    }
}
