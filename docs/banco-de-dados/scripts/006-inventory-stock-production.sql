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

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.production_orders (
        id uuid NOT NULL,
        technical_sheet_id uuid NOT NULL,
        technical_sheet_name_snapshot character varying(180) NOT NULL,
        technical_sheet_version integer NOT NULL,
        output_supply_item_id uuid,
        batch_multiplier numeric(18,4) NOT NULL,
        produced_quantity numeric(18,4) NOT NULL,
        yield_unit_code character varying(32) NOT NULL,
        batch_number character varying(64),
        expires_at_utc timestamp with time zone,
        total_production_cost numeric(18,4) NOT NULL,
        unit_production_cost numeric(18,4) NOT NULL,
        status integer NOT NULL,
        executed_by_user_id uuid,
        executed_at_utc timestamp with time zone NOT NULL,
        notes character varying(500),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_production_orders" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.purchase_receipts (
        id uuid NOT NULL,
        supplier_name character varying(160) NOT NULL,
        invoice_number character varying(80),
        notes character varying(500),
        received_by_user_id uuid,
        received_at_utc timestamp with time zone NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        total_currency character varying(3) NOT NULL,
        idempotency_key character varying(160),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_purchase_receipts" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.stock_balances (
        id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        unit_code character varying(32) NOT NULL,
        quantity_on_hand numeric(18,4) NOT NULL,
        minimum_stock_quantity numeric(18,4) NOT NULL,
        allow_negative_stock boolean NOT NULL,
        auto_deduct_on_order boolean NOT NULL,
        block_expired_lots boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_stock_balances" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.production_consumptions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        production_order_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        supply_name_snapshot character varying(180) NOT NULL,
        quantity_consumed_in_stock_unit numeric(18,4) NOT NULL,
        stock_unit_code character varying(32) NOT NULL,
        calculated_cost numeric(18,4) NOT NULL,
        CONSTRAINT "PK_production_consumptions" PRIMARY KEY (id),
        CONSTRAINT "FK_production_consumptions_production_orders_production_order_~" FOREIGN KEY (production_order_id) REFERENCES inventory.production_orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.purchase_receipt_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        purchase_receipt_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        supply_name_snapshot character varying(180) NOT NULL,
        purchased_quantity numeric(18,4) NOT NULL,
        purchase_unit_code character varying(32) NOT NULL,
        credited_stock_quantity numeric(18,4) NOT NULL,
        stock_unit_code character varying(32) NOT NULL,
        unit_price_amount numeric(18,2) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        total_cost_amount numeric(18,2) NOT NULL,
        total_cost_currency character varying(3) NOT NULL,
        batch_number character varying(64),
        manufactured_at_utc timestamp with time zone,
        expires_at_utc timestamp with time zone,
        CONSTRAINT "PK_purchase_receipt_items" PRIMARY KEY (id),
        CONSTRAINT "FK_purchase_receipt_items_purchase_receipts_purchase_receipt_id" FOREIGN KEY (purchase_receipt_id) REFERENCES inventory.purchase_receipts (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.stock_lots (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        stock_balance_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        batch_number character varying(64) NOT NULL,
        initial_quantity numeric(18,4) NOT NULL,
        remaining_quantity numeric(18,4) NOT NULL,
        unit_code character varying(32) NOT NULL,
        unit_cost numeric(18,4) NOT NULL,
        manufactured_at_utc timestamp with time zone,
        expires_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_stock_lots" PRIMARY KEY (id),
        CONSTRAINT "FK_stock_lots_stock_balances_stock_balance_id" FOREIGN KEY (stock_balance_id) REFERENCES inventory.stock_balances (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE TABLE inventory.stock_movements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        stock_balance_id uuid NOT NULL,
        supply_item_id uuid NOT NULL,
        stock_lot_id uuid,
        movement_type integer NOT NULL,
        quantity_delta numeric(18,4) NOT NULL,
        previous_quantity numeric(18,4) NOT NULL,
        resulting_quantity numeric(18,4) NOT NULL,
        unit_code character varying(32) NOT NULL,
        unit_cost numeric(18,4) NOT NULL,
        total_cost numeric(18,4) NOT NULL,
        reason character varying(300) NOT NULL,
        reference_type character varying(64),
        reference_id uuid,
        idempotency_key character varying(160),
        actor_user_id uuid,
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_stock_movements" PRIMARY KEY (id),
        CONSTRAINT "FK_stock_movements_stock_balances_stock_balance_id" FOREIGN KEY (stock_balance_id) REFERENCES inventory.stock_balances (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_production_consumptions_production_order_id" ON inventory.production_consumptions (production_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_production_orders_tenant_id_technical_sheet_id_executed_at_~" ON inventory.production_orders (tenant_id, technical_sheet_id, executed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_purchase_receipt_items_purchase_receipt_id" ON inventory.purchase_receipt_items (purchase_receipt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE UNIQUE INDEX "IX_purchase_receipts_tenant_id_idempotency_key" ON inventory.purchase_receipts (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_purchase_receipts_tenant_id_received_at_utc" ON inventory.purchase_receipts (tenant_id, received_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE UNIQUE INDEX "IX_stock_balances_tenant_id_supply_item_id" ON inventory.stock_balances (tenant_id, supply_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_stock_lots_stock_balance_id" ON inventory.stock_lots (stock_balance_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_stock_lots_tenant_id_supply_item_id_expires_at_utc" ON inventory.stock_lots (tenant_id, supply_item_id, expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_stock_movements_stock_balance_id" ON inventory.stock_movements (stock_balance_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE UNIQUE INDEX "IX_stock_movements_tenant_id_idempotency_key" ON inventory.stock_movements (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_stock_movements_tenant_id_reference_type_reference_id" ON inventory.stock_movements (tenant_id, reference_type, reference_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    CREATE INDEX "IX_stock_movements_tenant_id_supply_item_id_occurred_at_utc" ON inventory.stock_movements (tenant_id, supply_item_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."stock_balances" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."stock_balances" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."stock_balances";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."stock_balances"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."stock_lots" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."stock_lots" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."stock_lots";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."stock_lots"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."stock_movements" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."stock_movements" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."stock_movements";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."stock_movements"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."purchase_receipts" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."purchase_receipts" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."purchase_receipts";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."purchase_receipts"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."purchase_receipt_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."purchase_receipt_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."purchase_receipt_items";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."purchase_receipt_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."production_orders" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."production_orders" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."production_orders";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."production_orders"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    ALTER TABLE "inventory"."production_consumptions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "inventory"."production_consumptions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "inventory"."production_consumptions";
    CREATE POLICY "pitako_tenant_isolation" ON "inventory"."production_consumptions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM inventory.__ef_migrations_history WHERE "MigrationId" = '20261125101259_AddStockPurchasesAndProduction') THEN
    INSERT INTO inventory.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125101259_AddStockPurchasesAndProduction', '8.0.11');
    END IF;
END $EF$;
COMMIT;

