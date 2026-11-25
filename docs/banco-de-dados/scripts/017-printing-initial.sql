DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'printing') THEN
        CREATE SCHEMA printing;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS printing.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'printing') THEN
            CREATE SCHEMA printing;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE TABLE printing.print_jobs (
        id uuid NOT NULL,
        sector_type integer NOT NULL,
        document_kind integer NOT NULL,
        order_or_tab_reference character varying(64) NOT NULL,
        target_printer_id uuid NOT NULL,
        actual_printer_id uuid,
        used_fallback boolean NOT NULL,
        fallback_reason character varying(300),
        store_trade_name character varying(160) NOT NULL,
        store_cnpj character varying(24) NOT NULL,
        store_logo_ref character varying(300) NOT NULL,
        rendered_plain_text character varying(12000) NOT NULL,
        esc_pos_payload_base64 character varying(20000) NOT NULL,
        copies_count integer NOT NULL,
        status integer NOT NULL,
        attempt_count integer NOT NULL,
        last_error character varying(500),
        job_created_at_utc timestamp with time zone NOT NULL,
        printed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_print_jobs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE TABLE printing.printer_profiles (
        id uuid NOT NULL,
        store_unit_id uuid,
        printer_code character varying(40) NOT NULL,
        display_name character varying(120) NOT NULL,
        connection_kind integer NOT NULL,
        endpoint_or_port character varying(160) NOT NULL,
        paper_width_mm integer NOT NULL,
        status integer NOT NULL,
        backup_printer_id uuid,
        supports_logo_raster boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_printer_profiles" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE TABLE printing.sector_routing_rules (
        id uuid NOT NULL,
        store_unit_id uuid,
        sector_type integer NOT NULL,
        category_id uuid,
        primary_printer_id uuid NOT NULL,
        fallback_printer_id uuid,
        copies_count integer NOT NULL,
        auto_print_on_order_accepted boolean NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_sector_routing_rules" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE INDEX "IX_print_jobs_tenant_id_status_job_created_at_utc" ON printing.print_jobs (tenant_id, status, job_created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE UNIQUE INDEX "IX_printer_profiles_tenant_id_printer_code" ON printing.printer_profiles (tenant_id, printer_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    CREATE UNIQUE INDEX "IX_sector_routing_rules_tenant_id_sector_type_category_id" ON printing.sector_routing_rules (tenant_id, sector_type, category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    ALTER TABLE "printing"."printer_profiles" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "printing"."printer_profiles" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "printing"."printer_profiles";
    CREATE POLICY "pitako_tenant_isolation" ON "printing"."printer_profiles"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    ALTER TABLE "printing"."sector_routing_rules" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "printing"."sector_routing_rules" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "printing"."sector_routing_rules";
    CREATE POLICY "pitako_tenant_isolation" ON "printing"."sector_routing_rules"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    ALTER TABLE "printing"."print_jobs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "printing"."print_jobs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "printing"."print_jobs";
    CREATE POLICY "pitako_tenant_isolation" ON "printing"."print_jobs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM printing.__ef_migrations_history WHERE "MigrationId" = '20261125121132_InitialPrintingSchema') THEN
    INSERT INTO printing.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125121132_InitialPrintingSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

