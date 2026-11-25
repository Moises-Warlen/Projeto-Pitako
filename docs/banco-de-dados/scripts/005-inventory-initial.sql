DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
        CREATE SCHEMA inventory;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS inventory.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
            CREATE SCHEMA inventory;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.custom_units (
        id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(100) NOT NULL,
        dimension integer NOT NULL,
        factor_to_base_unit numeric(18,6) NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_custom_units" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.supply_items (
        id uuid NOT NULL,
        name character varying(180) NOT NULL,
        sku character varying(64),
        purchase_unit_code character varying(32) NOT NULL,
        purchase_quantity numeric(18,4) NOT NULL,
        content_unit_code character varying(32) NOT NULL,
        content_quantity_per_package numeric(18,4) NOT NULL,
        purchase_price_amount numeric(18,2) NOT NULL,
        purchase_price_currency character varying(3) NOT NULL,
        supplier_name character varying(160),
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_supply_items" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.technical_sheets (
        id uuid NOT NULL,
        product_id uuid,
        name character varying(180) NOT NULL,
        yield_quantity numeric(18,4) NOT NULL,
        yield_unit_code character varying(32) NOT NULL,
        notes character varying(1000),
        ingredients_total_cost numeric(18,4) NOT NULL,
        packagings_total_cost numeric(18,4) NOT NULL,
        total_cost numeric(18,4) NOT NULL,
        cost_per_portion numeric(18,4) NOT NULL,
        version integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_technical_sheets" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.supply_price_histories (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        previous_price_amount numeric(18,2) NOT NULL,
        previous_price_currency character varying(3) NOT NULL,
        new_price_amount numeric(18,2) NOT NULL,
        new_price_currency character varying(3) NOT NULL,
        supplier_name character varying(160),
        changed_by_user_id uuid,
        reason character varying(240) NOT NULL,
        changed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_supply_price_histories" PRIMARY KEY (id),
        CONSTRAINT "FK_supply_price_histories_supply_items_supply_item_id" FOREIGN KEY (supply_item_id) REFERENCES inventory.supply_items (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.technical_sheet_cost_snapshots (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        technical_sheet_id uuid NOT NULL,
        version integer NOT NULL,
        ingredients_cost numeric(18,4) NOT NULL,
        packagings_cost numeric(18,4) NOT NULL,
        total_cost numeric(18,4) NOT NULL,
        yield_quantity numeric(18,4) NOT NULL,
        cost_per_portion numeric(18,4) NOT NULL,
        trigger_reason character varying(300) NOT NULL,
        recorded_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_technical_sheet_cost_snapshots" PRIMARY KEY (id),
        CONSTRAINT "FK_technical_sheet_cost_snapshots_technical_sheets_technical_s~" FOREIGN KEY (technical_sheet_id) REFERENCES inventory.technical_sheets (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.technical_sheet_ingredients (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        technical_sheet_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        supply_item_name_snapshot character varying(180) NOT NULL,
        quantity numeric(18,4) NOT NULL,
        unit_code character varying(32) NOT NULL,
        calculated_cost numeric(18,4) NOT NULL,
        CONSTRAINT "PK_technical_sheet_ingredients" PRIMARY KEY (id),
        CONSTRAINT "FK_technical_sheet_ingredients_technical_sheets_technical_shee~" FOREIGN KEY (technical_sheet_id) REFERENCES inventory.technical_sheets (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE TABLE inventory.technical_sheet_packagings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        technical_sheet_id uuid NOT NULL,
        supply_item_id uuid,
        description character varying(180) NOT NULL,
        quantity numeric(18,4) NOT NULL,
        unit_code character varying(32) NOT NULL,
        calculated_cost numeric(18,4) NOT NULL,
        CONSTRAINT "PK_technical_sheet_packagings" PRIMARY KEY (id),
        CONSTRAINT "FK_technical_sheet_packagings_technical_sheets_technical_sheet~" FOREIGN KEY (technical_sheet_id) REFERENCES inventory.technical_sheets (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE UNIQUE INDEX "IX_custom_units_tenant_id_code" ON inventory.custom_units (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_supply_items_tenant_id_name" ON inventory.supply_items (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_supply_price_histories_supply_item_id" ON inventory.supply_price_histories (supply_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_supply_price_histories_tenant_id_supply_item_id" ON inventory.supply_price_histories (tenant_id, supply_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheet_cost_snapshots_technical_sheet_id" ON inventory.technical_sheet_cost_snapshots (technical_sheet_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheet_cost_snapshots_tenant_id_technical_sheet_id~" ON inventory.technical_sheet_cost_snapshots (tenant_id, technical_sheet_id, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheet_ingredients_technical_sheet_id" ON inventory.technical_sheet_ingredients (technical_sheet_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheet_ingredients_tenant_id_supply_item_id" ON inventory.technical_sheet_ingredients (tenant_id, supply_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheet_packagings_technical_sheet_id" ON inventory.technical_sheet_packagings (technical_sheet_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    CREATE INDEX "IX_technical_sheets_tenant_id_product_id" ON inventory.technical_sheets (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."custom_units" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."custom_units" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."custom_units";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."custom_units"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."supply_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."supply_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."supply_items";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."supply_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."supply_price_histories" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."supply_price_histories" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."supply_price_histories";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."supply_price_histories"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."technical_sheets" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."technical_sheets" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."technical_sheets";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."technical_sheets"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."technical_sheet_ingredients" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."technical_sheet_ingredients" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."technical_sheet_ingredients";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."technical_sheet_ingredients"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."technical_sheet_packagings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."technical_sheet_packagings" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."technical_sheet_packagings";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."technical_sheet_packagings"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    ALTER TABLE "inventory"."technical_sheet_cost_snapshots" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."technical_sheet_cost_snapshots" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."technical_sheet_cost_snapshots";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."technical_sheet_cost_snapshots"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125020251_InitialInventorySchema') THEN
    INSERT INTO inventory.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125020251_InitialInventorySchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

