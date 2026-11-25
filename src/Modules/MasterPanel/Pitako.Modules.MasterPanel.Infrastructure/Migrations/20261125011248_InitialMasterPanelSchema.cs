using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitako.Modules.MasterPanel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMasterPanelSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "master");

            migrationBuilder.CreateTable(
                name: "commercial_plans",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billing_cycle = table.Column<int>(type: "integer", nullable: false),
                    trial_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    establishments_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    establishments_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    units_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    units_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    marketplace_stores_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    marketplace_stores_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    users_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    users_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    devices_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    devices_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    concurrent_sessions_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    concurrent_sessions_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    monthly_orders_is_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    monthly_orders_max_allowed = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_accounts",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    billing_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "global_settings",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trial_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suspended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_to_saas_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_features", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_features_commercial_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "master",
                        principalTable: "commercial_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_modules",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_modules", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_modules_commercial_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "master",
                        principalTable: "commercial_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_period = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    due_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_gateway_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscription_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "master",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_plans_code",
                schema: "master",
                table: "commercial_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_accounts_document_number",
                schema: "master",
                table: "customer_accounts",
                column: "document_number");

            migrationBuilder.CreateIndex(
                name: "IX_global_settings_key",
                schema: "master",
                table: "global_settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_features_plan_id_feature_code",
                schema: "master",
                table: "plan_features",
                columns: new[] { "plan_id", "feature_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_modules_plan_id_module_code",
                schema: "master",
                table: "plan_modules",
                columns: new[] { "plan_id", "module_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoices_subscription_id",
                schema: "master",
                table: "subscription_invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_customer_account_id_status",
                schema: "master",
                table: "subscriptions",
                columns: new[] { "customer_account_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_accounts",
                schema: "master");

            migrationBuilder.DropTable(
                name: "global_settings",
                schema: "master");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "master");

            migrationBuilder.DropTable(
                name: "plan_modules",
                schema: "master");

            migrationBuilder.DropTable(
                name: "subscription_invoices",
                schema: "master");

            migrationBuilder.DropTable(
                name: "support_tickets",
                schema: "master");

            migrationBuilder.DropTable(
                name: "commercial_plans",
                schema: "master");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "master");
        }
    }
}
