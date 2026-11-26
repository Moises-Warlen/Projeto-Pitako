using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitako.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLossPercentToInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "loss_percent",
                schema: "inventory",
                table: "technical_sheet_ingredients",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "default_loss_percent",
                schema: "inventory",
                table: "supply_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "loss_percent",
                schema: "inventory",
                table: "technical_sheet_ingredients");

            migrationBuilder.DropColumn(
                name: "default_loss_percent",
                schema: "inventory",
                table: "supply_items");
        }
    }
}
