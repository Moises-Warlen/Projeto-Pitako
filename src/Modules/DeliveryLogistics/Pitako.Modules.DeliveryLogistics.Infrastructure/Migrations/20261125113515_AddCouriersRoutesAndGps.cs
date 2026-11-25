using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.DeliveryLogistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCouriersRoutesAndGps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "courier_gps_pings",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_dispatch_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    speed_kmh = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courier_gps_pings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_couriers",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vehicle_type = table.Column<int>(type: "integer", nullable: false),
                    license_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    commission_mode = table.Column<int>(type: "integer", nullable: false),
                    fixed_commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fixed_commission_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    freight_commission_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    availability = table.Column<int>(type: "integer", nullable: false),
                    last_known_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_known_longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_location_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_couriers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_dispatch_runs",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_estimated_distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    total_commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_commission_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_dispatch_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_run_stops",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_dispatch_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name_snapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    destination_latitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_longitude = table.Column<double>(type: "double precision", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    leg_distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    delivery_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    courier_commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    courier_commission_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_run_stops", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_run_stops_delivery_dispatch_runs_delivery_dispatch~",
                        column: x => x.delivery_dispatch_run_id,
                        principalSchema: "delivery",
                        principalTable: "delivery_dispatch_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_courier_gps_pings_tenant_id_courier_id_recorded_at_utc",
                schema: "delivery",
                table: "courier_gps_pings",
                columns: new[] { "tenant_id", "courier_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_couriers_tenant_id_availability",
                schema: "delivery",
                table: "delivery_couriers",
                columns: new[] { "tenant_id", "availability" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_dispatch_runs_tenant_id_courier_id_assigned_at_utc",
                schema: "delivery",
                table: "delivery_dispatch_runs",
                columns: new[] { "tenant_id", "courier_id", "assigned_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_run_stops_delivery_dispatch_run_id",
                schema: "delivery",
                table: "delivery_run_stops",
                column: "delivery_dispatch_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_run_stops_tenant_id_delivery_dispatch_run_id_stop_~",
                schema: "delivery",
                table: "delivery_run_stops",
                columns: new[] { "tenant_id", "delivery_dispatch_run_id", "stop_sequence" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CourierTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(DeliveryLogisticsDbContext.SchemaName, table));
            }
        }

        private static readonly string[] CourierTables =
        [
            "delivery_couriers",
            "delivery_dispatch_runs",
            "delivery_run_stops",
            "courier_gps_pings"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CourierTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(DeliveryLogisticsDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "courier_gps_pings",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_couriers",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_run_stops",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_dispatch_runs",
                schema: "delivery");
        }
    }
}
