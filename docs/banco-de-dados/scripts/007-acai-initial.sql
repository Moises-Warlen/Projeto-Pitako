DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'acai') THEN
        CREATE SCHEMA acai;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS acai.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'acai') THEN
            CREATE SCHEMA acai;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE TABLE acai.base_flavors (
        id uuid NOT NULL,
        name character varying(120) NOT NULL,
        extra_price_amount numeric(18,2) NOT NULL,
        extra_price_currency character varying(3) NOT NULL,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_base_flavors" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE TABLE acai.cup_sizes (
        id uuid NOT NULL,
        name character varying(120) NOT NULL,
        volume_ml integer NOT NULL,
        base_price_amount numeric(18,2) NOT NULL,
        base_price_currency character varying(3) NOT NULL,
        free_toppings_limit integer NOT NULL,
        min_free_toppings integer NOT NULL,
        counting_mode integer NOT NULL,
        max_paid_addons_per_cup integer,
        linked_product_id uuid,
        sku character varying(64),
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_cup_sizes" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE TABLE acai.free_toppings (
        id uuid NOT NULL,
        name character varying(140) NOT NULL,
        max_portions_per_cup integer NOT NULL,
        supply_item_id uuid,
        consumption_quantity_per_portion numeric(18,4),
        consumption_unit_code character varying(32),
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_free_toppings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE TABLE acai.paid_addons (
        id uuid NOT NULL,
        name character varying(140) NOT NULL,
        additional_price_amount numeric(18,2) NOT NULL,
        additional_price_currency character varying(3) NOT NULL,
        max_quantity_per_cup integer NOT NULL,
        supply_item_id uuid,
        consumption_quantity_per_unit numeric(18,4),
        consumption_unit_code character varying(32),
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_paid_addons" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE INDEX "IX_base_flavors_tenant_id_display_order" ON acai.base_flavors (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE INDEX "IX_cup_sizes_tenant_id_display_order" ON acai.cup_sizes (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE INDEX "IX_free_toppings_tenant_id_display_order" ON acai.free_toppings (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    CREATE INDEX "IX_paid_addons_tenant_id_display_order" ON acai.paid_addons (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    ALTER TABLE "acai"."cup_sizes" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "acai"."cup_sizes" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "acai"."cup_sizes";
    CREATE POLICY "pitako_tenant_isolation" ON "acai"."cup_sizes"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    ALTER TABLE "acai"."base_flavors" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "acai"."base_flavors" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "acai"."base_flavors";
    CREATE POLICY "pitako_tenant_isolation" ON "acai"."base_flavors"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    ALTER TABLE "acai"."free_toppings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "acai"."free_toppings" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "acai"."free_toppings";
    CREATE POLICY "pitako_tenant_isolation" ON "acai"."free_toppings"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    ALTER TABLE "acai"."paid_addons" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "acai"."paid_addons" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "acai"."paid_addons";
    CREATE POLICY "pitako_tenant_isolation" ON "acai"."paid_addons"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM acai.__ef_migrations_history WHERE "MigrationId" = '20261125103117_InitialAcaiBuilderSchema') THEN
    INSERT INTO acai.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125103117_InitialAcaiBuilderSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

