using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Catalog.Infrastructure.Persistence;

#nullable disable

namespace Pitako.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogCostPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cost_price_amount",
                schema: "catalog",
                table: "product_variations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cost_price_currency",
                schema: "catalog",
                table: "product_variations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "BRL");

            migrationBuilder.AddColumn<decimal>(
                name: "cost_price_amount",
                schema: "catalog",
                table: "modifier_options",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cost_price_currency",
                schema: "catalog",
                table: "modifier_options",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "BRL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "cost_price_amount", schema: "catalog", table: "product_variations");
            migrationBuilder.DropColumn(name: "cost_price_currency", schema: "catalog", table: "product_variations");
            migrationBuilder.DropColumn(name: "cost_price_amount", schema: "catalog", table: "modifier_options");
            migrationBuilder.DropColumn(name: "cost_price_currency", schema: "catalog", table: "modifier_options");
        }
    }
}
