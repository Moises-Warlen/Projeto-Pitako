using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.AcaiBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAcaiBuilderSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "acai");

            migrationBuilder.CreateTable(
                name: "base_flavors",
                schema: "acai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    extra_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    extra_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_base_flavors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cup_sizes",
                schema: "acai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    volume_ml = table.Column<int>(type: "integer", nullable: false),
                    base_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    free_toppings_limit = table.Column<int>(type: "integer", nullable: false),
                    min_free_toppings = table.Column<int>(type: "integer", nullable: false),
                    counting_mode = table.Column<int>(type: "integer", nullable: false),
                    max_paid_addons_per_cup = table.Column<int>(type: "integer", nullable: true),
                    linked_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cup_sizes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "free_toppings",
                schema: "acai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    max_portions_per_cup = table.Column<int>(type: "integer", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumption_quantity_per_portion = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    consumption_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_free_toppings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paid_addons",
                schema: "acai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    additional_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    additional_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    max_quantity_per_cup = table.Column<int>(type: "integer", nullable: false),
                    supply_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumption_quantity_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    consumption_unit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paid_addons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_base_flavors_tenant_id_display_order",
                schema: "acai",
                table: "base_flavors",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_cup_sizes_tenant_id_display_order",
                schema: "acai",
                table: "cup_sizes",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_free_toppings_tenant_id_display_order",
                schema: "acai",
                table: "free_toppings",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_paid_addons_tenant_id_display_order",
                schema: "acai",
                table: "paid_addons",
                columns: new[] { "tenant_id", "display_order" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in AcaiTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(AcaiBuilderDbContext.SchemaName, table));
            }
        }

        private static readonly string[] AcaiTables =
        [
            "cup_sizes",
            "base_flavors",
            "free_toppings",
            "paid_addons"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in AcaiTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(AcaiBuilderDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "base_flavors",
                schema: "acai");

            migrationBuilder.DropTable(
                name: "cup_sizes",
                schema: "acai");

            migrationBuilder.DropTable(
                name: "free_toppings",
                schema: "acai");

            migrationBuilder.DropTable(
                name: "paid_addons",
                schema: "acai");
        }
    }
}
