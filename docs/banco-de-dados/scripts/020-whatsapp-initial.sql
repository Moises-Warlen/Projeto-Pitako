DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'whatsapp') THEN
        CREATE SCHEMA whatsapp;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS whatsapp.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'whatsapp') THEN
            CREATE SCHEMA whatsapp;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE TABLE whatsapp.bot_rules (
        id uuid NOT NULL,
        channel_config_id uuid NOT NULL,
        rule_name character varying(120) NOT NULL,
        trigger_keyword character varying(120) NOT NULL,
        match_mode integer NOT NULL,
        reply_template character varying(2000) NOT NULL,
        include_menu_link boolean NOT NULL,
        escalate_to_human boolean NOT NULL,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_bot_rules" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE TABLE whatsapp.channel_configs (
        id uuid NOT NULL,
        store_unit_id uuid,
        store_trade_name character varying(160) NOT NULL,
        display_phone_number character varying(40) NOT NULL,
        phone_number_id character varying(80) NOT NULL,
        business_account_id character varying(80) NOT NULL,
        encrypted_access_token_blob character varying(4000) NOT NULL,
        encrypted_app_secret_blob character varying(4000) NOT NULL,
        webhook_verify_token_hash character varying(128) NOT NULL,
        auto_reply_mode integer NOT NULL,
        menu_url character varying(300),
        is_active boolean NOT NULL,
        last_webhook_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_channel_configs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE TABLE whatsapp.conversations (
        id uuid NOT NULL,
        channel_config_id uuid NOT NULL,
        customer_phone character varying(40) NOT NULL,
        customer_name character varying(160) NOT NULL,
        status integer NOT NULL,
        assigned_agent_user_id uuid,
        human_handoff_reason character varying(300),
        last_message_preview character varying(160) NOT NULL,
        unread_count integer NOT NULL,
        started_at_utc timestamp with time zone NOT NULL,
        last_interaction_at_utc timestamp with time zone NOT NULL,
        resolved_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_conversations" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE TABLE whatsapp.messages (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        conversation_id uuid NOT NULL,
        external_message_id character varying(160),
        direction integer NOT NULL,
        sender_user_id uuid,
        content_text character varying(4000) NOT NULL,
        delivery_status integer NOT NULL,
        grounded_by_facts boolean NOT NULL,
        delivery_error character varying(500),
        sent_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_messages" PRIMARY KEY (id),
        CONSTRAINT "FK_messages_conversations_conversation_id" FOREIGN KEY (conversation_id) REFERENCES whatsapp.conversations (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE INDEX "IX_bot_rules_tenant_id_channel_config_id_display_order" ON whatsapp.bot_rules (tenant_id, channel_config_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE UNIQUE INDEX "IX_channel_configs_tenant_id_phone_number_id" ON whatsapp.channel_configs (tenant_id, phone_number_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE UNIQUE INDEX "IX_conversations_tenant_id_channel_config_id_customer_phone" ON whatsapp.conversations (tenant_id, channel_config_id, customer_phone);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE INDEX "IX_messages_conversation_id" ON whatsapp.messages (conversation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE INDEX "IX_messages_tenant_id_conversation_id_sent_at_utc" ON whatsapp.messages (tenant_id, conversation_id, sent_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    CREATE UNIQUE INDEX "IX_messages_tenant_id_external_message_id" ON whatsapp.messages (tenant_id, external_message_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    ALTER TABLE "whatsapp"."channel_configs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "whatsapp"."channel_configs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "whatsapp"."channel_configs";
    CREATE POLICY "pitako_tenant_isolation" ON "whatsapp"."channel_configs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    ALTER TABLE "whatsapp"."bot_rules" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "whatsapp"."bot_rules" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "whatsapp"."bot_rules";
    CREATE POLICY "pitako_tenant_isolation" ON "whatsapp"."bot_rules"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    ALTER TABLE "whatsapp"."conversations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "whatsapp"."conversations" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "whatsapp"."conversations";
    CREATE POLICY "pitako_tenant_isolation" ON "whatsapp"."conversations"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    ALTER TABLE "whatsapp"."messages" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "whatsapp"."messages" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "whatsapp"."messages";
    CREATE POLICY "pitako_tenant_isolation" ON "whatsapp"."messages"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM whatsapp.__ef_migrations_history WHERE "MigrationId" = '20261125124003_InitialWhatsAppSchema') THEN
    INSERT INTO whatsapp.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125124003_InitialWhatsAppSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

