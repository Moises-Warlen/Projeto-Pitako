using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Marketplaces.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Marketplaces.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMarketplacesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketplaces");

            migrationBuilder.CreateTable(
                name: "catalog_mappings",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    internal_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    internal_item_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_sku_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_paused_at_partner = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    last_synced_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    last_synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbound_events",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    store_binding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_event_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_merchant_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    raw_payload_json = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    signature_verified = table.Column<bool>(type: "boolean", nullable: false),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    normalized_customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    normalized_fulfillment = table.Column<int>(type: "integer", nullable: true),
                    normalized_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    normalized_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    normalized_items_json = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    internal_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_accounts",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    auth_kind = table.Column<int>(type: "integer", nullable: false),
                    account_label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    client_or_merchant_identifier = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    encrypted_credentials_blob = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_health_check_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_bindings",
                schema: "marketplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_merchant_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_store_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    auto_accept_orders = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_bindings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_mappings_tenant_id_store_binding_id_internal_produc~",
                schema: "marketplaces",
                table: "catalog_mappings",
                columns: new[] { "tenant_id", "store_binding_id", "internal_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_events_tenant_id_provider_external_event_id",
                schema: "marketplaces",
                table: "inbound_events",
                columns: new[] { "tenant_id", "provider", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_accounts_tenant_id_provider_client_or_merchant_iden~",
                schema: "marketplaces",
                table: "partner_accounts",
                columns: new[] { "tenant_id", "provider", "client_or_merchant_identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_store_bindings_tenant_id_provider_external_merchant_id",
                schema: "marketplaces",
                table: "store_bindings",
                columns: new[] { "tenant_id", "provider", "external_merchant_id" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in MarketplaceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(MarketplacesDbContext.SchemaName, table));
            }
        }

        private static readonly string[] MarketplaceTables =
        [
            "partner_accounts",
            "store_bindings",
            "inbound_events",
            "catalog_mappings"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in MarketplaceTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(MarketplacesDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "catalog_mappings",
                schema: "marketplaces");

            migrationBuilder.DropTable(
                name: "inbound_events",
                schema: "marketplaces");

            migrationBuilder.DropTable(
                name: "partner_accounts",
                schema: "marketplaces");

            migrationBuilder.DropTable(
                name: "store_bindings",
                schema: "marketplaces");
        }
    }
}
