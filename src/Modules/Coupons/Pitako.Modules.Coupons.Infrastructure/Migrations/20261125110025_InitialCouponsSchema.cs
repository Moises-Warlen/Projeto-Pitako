using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Coupons.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Coupons.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCouponsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "coupons");

            migrationBuilder.CreateTable(
                name: "coupons",
                schema: "coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    discount_type = table.Column<int>(type: "integer", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    max_discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    minimum_order_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    minimum_order_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_total_uses = table.Column<int>(type: "integer", nullable: true),
                    max_uses_per_customer = table.Column<int>(type: "integer", nullable: true),
                    can_combine_with_promotions = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coupon_eligibility_rules",
                schema: "coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_eligibility_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_coupon_eligibility_rules_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalSchema: "coupons",
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                schema: "coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_identifier = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    discount_applied_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_applied_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_coupon_redemptions_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalSchema: "coupons",
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_eligibility_rules_coupon_id",
                schema: "coupons",
                table: "coupon_eligibility_rules",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "IX_coupon_eligibility_rules_tenant_id_coupon_id",
                schema: "coupons",
                table: "coupon_eligibility_rules",
                columns: new[] { "tenant_id", "coupon_id" });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_redemptions_coupon_id",
                schema: "coupons",
                table: "coupon_redemptions",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "IX_coupon_redemptions_tenant_id_coupon_id_customer_identifier",
                schema: "coupons",
                table: "coupon_redemptions",
                columns: new[] { "tenant_id", "coupon_id", "customer_identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_redemptions_tenant_id_coupon_id_order_id",
                schema: "coupons",
                table: "coupon_redemptions",
                columns: new[] { "tenant_id", "coupon_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_coupons_tenant_id_code",
                schema: "coupons",
                table: "coupons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CouponTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(CouponsDbContext.SchemaName, table));
            }
        }

        private static readonly string[] CouponTables =
        [
            "coupons",
            "coupon_eligibility_rules",
            "coupon_redemptions"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in CouponTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(CouponsDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "coupon_eligibility_rules",
                schema: "coupons");

            migrationBuilder.DropTable(
                name: "coupon_redemptions",
                schema: "coupons");

            migrationBuilder.DropTable(
                name: "coupons",
                schema: "coupons");
        }
    }
}
