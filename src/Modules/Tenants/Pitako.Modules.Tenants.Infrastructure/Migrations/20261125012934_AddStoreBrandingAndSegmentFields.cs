using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Tenants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreBrandingAndSegmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "tenants",
                table: "tenants",
                type: "character varying(350)",
                maxLength: 350,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "tenants",
                table: "tenants",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "tenants",
                table: "tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_account_id",
                schema: "tenants",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "tenants",
                table: "store_units",
                type: "character varying(350)",
                maxLength: 350,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "tenants",
                table: "store_units",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                schema: "tenants",
                table: "store_units",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "store_visual_identities",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    draft_cover_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    draft_slogan = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    draft_primary_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    draft_secondary_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    draft_accent_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    draft_menu_layout_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    published_logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    published_cover_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    published_slogan = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    published_primary_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    published_secondary_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    published_accent_color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    published_menu_layout_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    published_version = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    has_unpublished_changes = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_visual_identities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_banners",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visual_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_draft = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_banners", x => x.id);
                    table.ForeignKey(
                        name: "FK_store_banners_store_visual_identities_visual_identity_id",
                        column: x => x.visual_identity_id,
                        principalSchema: "tenants",
                        principalTable: "store_visual_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenants_customer_account_id",
                schema: "tenants",
                table: "tenants",
                column: "customer_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_store_banners_visual_identity_id_is_draft_display_order",
                schema: "tenants",
                table: "store_banners",
                columns: new[] { "visual_identity_id", "is_draft", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_store_visual_identities_tenant_id",
                schema: "tenants",
                table: "store_visual_identities",
                column: "tenant_id",
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(TenantsDbContext.SchemaName, "store_visual_identities"));
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(TenantsDbContext.SchemaName, "store_banners"));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(TenantsDbContext.SchemaName, "store_banners"));
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(TenantsDbContext.SchemaName, "store_visual_identities"));
            }

            migrationBuilder.DropTable(
                name: "store_banners",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "store_visual_identities",
                schema: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_tenants_customer_account_id",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "customer_account_id",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "tenants",
                table: "store_units");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "tenants",
                table: "store_units");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                schema: "tenants",
                table: "store_units");
        }
    }
}
