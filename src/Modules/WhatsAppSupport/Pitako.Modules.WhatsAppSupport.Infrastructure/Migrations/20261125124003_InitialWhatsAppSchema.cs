using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.WhatsAppSupport.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWhatsAppSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "whatsapp");

            migrationBuilder.CreateTable(
                name: "bot_rules",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trigger_keyword = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    match_mode = table.Column<int>(type: "integer", nullable: false),
                    reply_template = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    include_menu_link = table.Column<bool>(type: "boolean", nullable: false),
                    escalate_to_human = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channel_configs",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_trade_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    display_phone_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    phone_number_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    business_account_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    encrypted_access_token_blob = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    encrypted_app_secret_blob = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    webhook_verify_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    auto_reply_mode = table.Column<int>(type: "integer", nullable: false),
                    menu_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_webhook_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_agent_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    human_handoff_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_message_preview = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_interaction_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_message_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    delivery_status = table.Column<int>(type: "integer", nullable: false),
                    grounded_by_facts = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "whatsapp",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_rules_tenant_id_channel_config_id_display_order",
                schema: "whatsapp",
                table: "bot_rules",
                columns: new[] { "tenant_id", "channel_config_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_channel_configs_tenant_id_phone_number_id",
                schema: "whatsapp",
                table: "channel_configs",
                columns: new[] { "tenant_id", "phone_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_tenant_id_channel_config_id_customer_phone",
                schema: "whatsapp",
                table: "conversations",
                columns: new[] { "tenant_id", "channel_config_id", "customer_phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_id",
                schema: "whatsapp",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_tenant_id_conversation_id_sent_at_utc",
                schema: "whatsapp",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_tenant_id_external_message_id",
                schema: "whatsapp",
                table: "messages",
                columns: new[] { "tenant_id", "external_message_id" },
                unique: true);

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in WhatsAppTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(WhatsAppDbContext.SchemaName, table));
            }
        }

        private static readonly string[] WhatsAppTables =
        [
            "channel_configs",
            "bot_rules",
            "conversations",
            "messages"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                foreach (var table in WhatsAppTables)
                    migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(WhatsAppDbContext.SchemaName, table));
            }
            migrationBuilder.DropTable(
                name: "bot_rules",
                schema: "whatsapp");

            migrationBuilder.DropTable(
                name: "channel_configs",
                schema: "whatsapp");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "whatsapp");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "whatsapp");
        }
    }
}
