using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitako.Modules.Tenants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreHoursAndOperatingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "draft_announcement_text",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(350)",
                maxLength: 350,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_instagram_handle",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_operating_hours_json",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(2500)",
                maxLength: 2500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_whatsapp_number",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_temporarily_paused",
                schema: "tenants",
                table: "store_visual_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pause_reason",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_announcement_text",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(350)",
                maxLength: 350,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_instagram_handle",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_operating_hours_json",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(2500)",
                maxLength: 2500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_whatsapp_number",
                schema: "tenants",
                table: "store_visual_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "draft_announcement_text",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "draft_instagram_handle",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "draft_operating_hours_json",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "draft_whatsapp_number",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "is_temporarily_paused",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "pause_reason",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "published_announcement_text",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "published_instagram_handle",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "published_operating_hours_json",
                schema: "tenants",
                table: "store_visual_identities");

            migrationBuilder.DropColumn(
                name: "published_whatsapp_number",
                schema: "tenants",
                table: "store_visual_identities");
        }
    }
}
