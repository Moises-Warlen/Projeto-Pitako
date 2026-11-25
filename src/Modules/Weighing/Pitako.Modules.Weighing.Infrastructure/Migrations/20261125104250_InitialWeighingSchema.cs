using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Weighing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWeighingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "weighing");

            migrationBuilder.CreateTable(
                name: "scale_devices",
                schema: "weighing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    manufacturer = table.Column<int>(type: "integer", nullable: false),
                    connection_type = table.Column<int>(type: "integer", nullable: false),
                    protocol_profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    endpoint_or_port = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    baud_rate = table.Column<int>(type: "integer", nullable: false),
                    allow_manual_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scale_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tare_presets",
                schema: "weighing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tare_weight_grams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tare_presets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weighing_records",
                schema: "weighing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scale_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tare_preset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name_snapshot = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    capture_mode = table.Column<int>(type: "integer", nullable: false),
                    gross_weight_grams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    tare_weight_grams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    net_weight_grams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    price_per_kg_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_per_kg_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    manual_fallback_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    raw_scale_payload = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    linked_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weighing_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scale_devices_tenant_id_is_active",
                schema: "weighing",
                table: "scale_devices",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_tare_presets_tenant_id_is_active",
                schema: "weighing",
                table: "tare_presets",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_weighing_records_tenant_id_captured_at_utc",
                schema: "weighing",
                table: "weighing_records",
                columns: new[] { "tenant_id", "captured_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_weighing_records_tenant_id_linked_order_id",
                schema: "weighing",
                table: "weighing_records",
                columns: new[] { "tenant_id", "linked_order_id" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in WeighingTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(WeighingDbContext.SchemaName, table));
            }
        }

        private static readonly string[] WeighingTables =
        [
            "scale_devices",
            "tare_presets",
            "weighing_records"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in WeighingTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(WeighingDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "scale_devices",
                schema: "weighing");

            migrationBuilder.DropTable(
                name: "tare_presets",
                schema: "weighing");

            migrationBuilder.DropTable(
                name: "weighing_records",
                schema: "weighing");
        }
    }
}
