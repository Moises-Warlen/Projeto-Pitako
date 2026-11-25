DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'dining') THEN
        CREATE SCHEMA dining;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS dining.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'dining') THEN
            CREATE SCHEMA dining;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.dining_sectors (
        id uuid NOT NULL,
        store_unit_id uuid,
        name character varying(120) NOT NULL,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_dining_sectors" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.dining_tables (
        id uuid NOT NULL,
        sector_id uuid NOT NULL,
        table_code character varying(32) NOT NULL,
        capacity integer NOT NULL,
        status integer NOT NULL,
        current_tab_id uuid,
        qr_code_token character varying(64) NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_dining_tables" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.dining_tabs (
        id uuid NOT NULL,
        tab_code character varying(32) NOT NULL,
        dining_table_id uuid,
        waiter_user_id uuid NOT NULL,
        customer_name character varying(160) NOT NULL,
        guest_count integer NOT NULL,
        service_charge_percent numeric(5,2) NOT NULL,
        status integer NOT NULL,
        items_subtotal_amount numeric(18,2) NOT NULL,
        items_subtotal_currency character varying(3) NOT NULL,
        service_charge_amount numeric(18,2) NOT NULL,
        service_charge_currency character varying(3) NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        total_currency character varying(3) NOT NULL,
        paid_amount numeric(18,2) NOT NULL,
        paid_currency character varying(3) NOT NULL,
        opened_at_utc timestamp with time zone NOT NULL,
        closed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_dining_tabs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.table_reservations (
        id uuid NOT NULL,
        dining_table_id uuid NOT NULL,
        customer_name character varying(160) NOT NULL,
        customer_phone character varying(40) NOT NULL,
        party_size integer NOT NULL,
        reserved_for_utc timestamp with time zone NOT NULL,
        duration_minutes integer NOT NULL,
        notes character varying(300),
        status integer NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_table_reservations" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.dining_tab_audit_events (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        dining_tab_id uuid NOT NULL,
        action character varying(80) NOT NULL,
        details character varying(400) NOT NULL,
        actor_user_id uuid,
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_dining_tab_audit_events" PRIMARY KEY (id),
        CONSTRAINT "FK_dining_tab_audit_events_dining_tabs_dining_tab_id" FOREIGN KEY (dining_tab_id) REFERENCES dining.dining_tabs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE TABLE dining.dining_tab_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        dining_tab_id uuid NOT NULL,
        product_id uuid,
        item_title character varying(200) NOT NULL,
        customization_summary character varying(600) NOT NULL,
        unit_price_amount numeric(18,2) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        quantity integer NOT NULL,
        line_subtotal_amount numeric(18,2) NOT NULL,
        line_subtotal_currency character varying(3) NOT NULL,
        status integer NOT NULL,
        added_by_user_id uuid,
        added_at_utc timestamp with time zone NOT NULL,
        cancellation_reason character varying(300),
        CONSTRAINT "PK_dining_tab_items" PRIMARY KEY (id),
        CONSTRAINT "FK_dining_tab_items_dining_tabs_dining_tab_id" FOREIGN KEY (dining_tab_id) REFERENCES dining.dining_tabs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_sectors_tenant_id_display_order" ON dining.dining_sectors (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_tab_audit_events_dining_tab_id" ON dining.dining_tab_audit_events (dining_tab_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_tab_audit_events_tenant_id_dining_tab_id_occurred_at~" ON dining.dining_tab_audit_events (tenant_id, dining_tab_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_tab_items_dining_tab_id" ON dining.dining_tab_items (dining_tab_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_tab_items_tenant_id_dining_tab_id" ON dining.dining_tab_items (tenant_id, dining_tab_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE UNIQUE INDEX "IX_dining_tables_tenant_id_qr_code_token" ON dining.dining_tables (tenant_id, qr_code_token);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE UNIQUE INDEX "IX_dining_tables_tenant_id_table_code" ON dining.dining_tables (tenant_id, table_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_dining_tabs_tenant_id_status_opened_at_utc" ON dining.dining_tabs (tenant_id, status, opened_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    CREATE INDEX "IX_table_reservations_tenant_id_dining_table_id_reserved_for_u~" ON dining.table_reservations (tenant_id, dining_table_id, reserved_for_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."dining_sectors" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."dining_sectors" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."dining_sectors";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."dining_sectors"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."dining_tables" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."dining_tables" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."dining_tables";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."dining_tables"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."table_reservations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."table_reservations" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."table_reservations";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."table_reservations"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."dining_tabs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."dining_tabs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."dining_tabs";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."dining_tabs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."dining_tab_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."dining_tab_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."dining_tab_items";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."dining_tab_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    ALTER TABLE "dining"."dining_tab_audit_events" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."dining_tab_audit_events" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."dining_tab_audit_events";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."dining_tab_audit_events"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125114821_InitialDiningHallSchema') THEN
    INSERT INTO dining.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125114821_InitialDiningHallSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE dining.dining_tabs ADD origin integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE TABLE dining.qr_table_sessions (
        id uuid NOT NULL,
        session_token character varying(80) NOT NULL,
        dining_table_id uuid NOT NULL,
        dining_tab_id uuid NOT NULL,
        table_code character varying(32) NOT NULL,
        customer_name character varying(160) NOT NULL,
        status integer NOT NULL,
        opened_at_utc timestamp with time zone NOT NULL,
        last_activity_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_qr_table_sessions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE TABLE dining.totem_order_sessions (
        id uuid NOT NULL,
        totem_terminal_id uuid NOT NULL,
        session_token character varying(80) NOT NULL,
        fulfillment_choice integer NOT NULL,
        customer_name character varying(160),
        pager_or_table_tag character varying(40),
        status integer NOT NULL,
        payment_choice integer,
        pickup_call_code character varying(32),
        dispatched_order_number character varying(32),
        items_subtotal_amount numeric(18,2) NOT NULL,
        items_subtotal_currency character varying(3) NOT NULL,
        idempotency_key character varying(120),
        started_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        confirmed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_totem_order_sessions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE TABLE dining.totem_terminals (
        id uuid NOT NULL,
        store_unit_id uuid,
        terminal_code character varying(32) NOT NULL,
        display_name character varying(120) NOT NULL,
        status integer NOT NULL,
        default_fulfillment_choice integer NOT NULL,
        require_customer_name boolean NOT NULL,
        require_table_or_pager_number boolean NOT NULL,
        idle_timeout_seconds integer NOT NULL,
        last_heartbeat_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_totem_terminals" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE TABLE dining.waiter_calls (
        id uuid NOT NULL,
        dining_table_id uuid NOT NULL,
        table_code character varying(32) NOT NULL,
        dining_tab_id uuid,
        qr_table_session_id uuid,
        call_type integer NOT NULL,
        customer_notes character varying(300),
        status integer NOT NULL,
        requested_at_utc timestamp with time zone NOT NULL,
        acknowledged_by_user_id uuid,
        acknowledged_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_waiter_calls" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE TABLE dining.totem_order_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        totem_order_session_id uuid NOT NULL,
        product_id uuid,
        acai_cup_size_id uuid,
        item_title character varying(200) NOT NULL,
        customization_summary character varying(600) NOT NULL,
        canonical_selections_json character varying(4000) NOT NULL,
        unit_price_amount numeric(18,2) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        quantity integer NOT NULL,
        line_subtotal_amount numeric(18,2) NOT NULL,
        line_subtotal_currency character varying(3) NOT NULL,
        customer_notes character varying(300),
        CONSTRAINT "PK_totem_order_lines" PRIMARY KEY (id),
        CONSTRAINT "FK_totem_order_lines_totem_order_sessions_totem_order_session_~" FOREIGN KEY (totem_order_session_id) REFERENCES dining.totem_order_sessions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE INDEX "IX_qr_table_sessions_tenant_id_dining_table_id_status" ON dining.qr_table_sessions (tenant_id, dining_table_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE UNIQUE INDEX "IX_qr_table_sessions_tenant_id_session_token" ON dining.qr_table_sessions (tenant_id, session_token);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE INDEX "IX_totem_order_lines_tenant_id_totem_order_session_id" ON dining.totem_order_lines (tenant_id, totem_order_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE INDEX "IX_totem_order_lines_totem_order_session_id" ON dining.totem_order_lines (totem_order_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE UNIQUE INDEX "IX_totem_order_sessions_tenant_id_idempotency_key" ON dining.totem_order_sessions (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE UNIQUE INDEX "IX_totem_order_sessions_tenant_id_session_token" ON dining.totem_order_sessions (tenant_id, session_token);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE UNIQUE INDEX "IX_totem_terminals_tenant_id_terminal_code" ON dining.totem_terminals (tenant_id, terminal_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    CREATE INDEX "IX_waiter_calls_tenant_id_status_requested_at_utc" ON dining.waiter_calls (tenant_id, status, requested_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE "dining"."qr_table_sessions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."qr_table_sessions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."qr_table_sessions";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."qr_table_sessions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE "dining"."waiter_calls" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."waiter_calls" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."waiter_calls";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."waiter_calls"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE "dining"."totem_terminals" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."totem_terminals" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."totem_terminals";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."totem_terminals"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE "dining"."totem_order_sessions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."totem_order_sessions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."totem_order_sessions";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."totem_order_sessions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    ALTER TABLE "dining"."totem_order_lines" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "dining"."totem_order_lines" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "dining"."totem_order_lines";
    CREATE POLICY "pitako_tenant_isolation" ON "dining"."totem_order_lines"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM dining.__ef_migrations_history WHERE "MigrationId" = '20261125120004_AddSelfServiceQrAndTotem') THEN
    INSERT INTO dining.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125120004_AddSelfServiceQrAndTotem', '8.0.11');
    END IF;
END $EF$;
COMMIT;

