using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitako.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentAndExtendedCartPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "selections_payload_json",
                schema: "orders",
                table: "shopping_cart_items",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<bool>(
                name: "lgpd_consent_accepted",
                schema: "orders",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "orders",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "selections_payload_json",
                schema: "orders",
                table: "order_items",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lgpd_consent_accepted",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "orders",
                table: "orders");

            migrationBuilder.AlterColumn<string>(
                name: "selections_payload_json",
                schema: "orders",
                table: "shopping_cart_items",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16000)",
                oldMaxLength: 16000);

            migrationBuilder.AlterColumn<string>(
                name: "selections_payload_json",
                schema: "orders",
                table: "order_items",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16000)",
                oldMaxLength: 16000);
        }
    }
}
