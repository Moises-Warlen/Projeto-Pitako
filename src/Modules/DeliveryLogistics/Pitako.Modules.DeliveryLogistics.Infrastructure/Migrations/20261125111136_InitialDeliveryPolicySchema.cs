using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.DeliveryLogistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeliveryPolicySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.CreateTable(
                name: "delivery_policies",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_latitude = table.Column<double>(type: "double precision", nullable: false),
                    origin_longitude = table.Column<double>(type: "double precision", nullable: false),
                    max_radius_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    calculation_mode = table.Column<int>(type: "integer", nullable: false),
                    base_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    included_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    fee_per_km_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee_per_km_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    base_estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    minimum_order_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    minimum_order_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    free_shipping_threshold_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    free_shipping_threshold_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    is_delivery_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    allow_pickup = table.Column<bool>(type: "boolean", nullable: false),
                    is_temporarily_paused = table.Column<bool>(type: "boolean", nullable: false),
                    pause_reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_distance_tiers",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    max_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_distance_tiers", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_distance_tiers_delivery_policies_delivery_policy_id",
                        column: x => x.delivery_policy_id,
                        principalSchema: "delivery",
                        principalTable: "delivery_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zone_rules",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    zip_code_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zone_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_zone_rules_delivery_policies_delivery_policy_id",
                        column: x => x.delivery_policy_id,
                        principalSchema: "delivery",
                        principalTable: "delivery_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_distance_tiers_delivery_policy_id",
                schema: "delivery",
                table: "delivery_distance_tiers",
                column: "delivery_policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_distance_tiers_tenant_id_delivery_policy_id_min_km",
                schema: "delivery",
                table: "delivery_distance_tiers",
                columns: new[] { "tenant_id", "delivery_policy_id", "min_km" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_policies_tenant_id_store_unit_id",
                schema: "delivery",
                table: "delivery_policies",
                columns: new[] { "tenant_id", "store_unit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zone_rules_delivery_policy_id",
                schema: "delivery",
                table: "delivery_zone_rules",
                column: "delivery_policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zone_rules_tenant_id_delivery_policy_id_zone_name",
                schema: "delivery",
                table: "delivery_zone_rules",
                columns: new[] { "tenant_id", "delivery_policy_id", "zone_name" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in DeliveryTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(DeliveryLogisticsDbContext.SchemaName, table));
            }
        }

        private static readonly string[] DeliveryTables =
        [
            "delivery_policies",
            "delivery_distance_tiers",
            "delivery_zone_rules"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in DeliveryTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(DeliveryLogisticsDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "delivery_distance_tiers",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_zone_rules",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_policies",
                schema: "delivery");
        }
    }
}
