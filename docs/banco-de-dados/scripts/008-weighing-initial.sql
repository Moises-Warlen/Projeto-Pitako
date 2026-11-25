DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'weighing') THEN
        CREATE SCHEMA weighing;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS weighing.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'weighing') THEN
            CREATE SCHEMA weighing;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE TABLE weighing.scale_devices (
        id uuid NOT NULL,
        name character varying(120) NOT NULL,
        manufacturer integer NOT NULL,
        connection_type integer NOT NULL,
        protocol_profile character varying(64) NOT NULL,
        endpoint_or_port character varying(160) NOT NULL,
        baud_rate integer NOT NULL,
        allow_manual_fallback boolean NOT NULL,
        is_default boolean NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_scale_devices" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE TABLE weighing.tare_presets (
        id uuid NOT NULL,
        name character varying(120) NOT NULL,
        tare_weight_grams numeric(18,3) NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_tare_presets" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE TABLE weighing.weighing_records (
        id uuid NOT NULL,
        scale_device_id uuid,
        tare_preset_id uuid,
        product_id uuid,
        product_name_snapshot character varying(180) NOT NULL,
        capture_mode integer NOT NULL,
        gross_weight_grams numeric(18,3) NOT NULL,
        tare_weight_grams numeric(18,3) NOT NULL,
        net_weight_grams numeric(18,3) NOT NULL,
        price_per_kg_amount numeric(18,2) NOT NULL,
        price_per_kg_currency character varying(3) NOT NULL,
        total_price_amount numeric(18,2) NOT NULL,
        total_price_currency character varying(3) NOT NULL,
        operator_user_id uuid NOT NULL,
        captured_at_utc timestamp with time zone NOT NULL,
        manual_fallback_reason character varying(300),
        raw_scale_payload character varying(200),
        linked_order_id uuid,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_weighing_records" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE INDEX "IX_scale_devices_tenant_id_is_active" ON weighing.scale_devices (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE INDEX "IX_tare_presets_tenant_id_is_active" ON weighing.tare_presets (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE INDEX "IX_weighing_records_tenant_id_captured_at_utc" ON weighing.weighing_records (tenant_id, captured_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    CREATE INDEX "IX_weighing_records_tenant_id_linked_order_id" ON weighing.weighing_records (tenant_id, linked_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    ALTER TABLE "weighing"."scale_devices" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "weighing"."scale_devices" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "weighing"."scale_devices";
    CREATE POLICY "pitako_tenant_isolation" ON "weighing"."scale_devices"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    ALTER TABLE "weighing"."tare_presets" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "weighing"."tare_presets" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "weighing"."tare_presets";
    CREATE POLICY "pitako_tenant_isolation" ON "weighing"."tare_presets"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    ALTER TABLE "weighing"."weighing_records" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "weighing"."weighing_records" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "weighing"."weighing_records";
    CREATE POLICY "pitako_tenant_isolation" ON "weighing"."weighing_records"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM weighing.__ef_migrations_history WHERE "MigrationId" = '20261125104250_InitialWeighingSchema') THEN
    INSERT INTO weighing.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125104250_InitialWeighingSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

