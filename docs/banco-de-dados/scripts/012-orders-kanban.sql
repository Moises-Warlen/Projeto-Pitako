DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'orders') THEN
        CREATE SCHEMA orders;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS orders.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'orders') THEN
            CREATE SCHEMA orders;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    CREATE TABLE orders.shopping_carts (
        id uuid NOT NULL,
        cart_session_key character varying(160) NOT NULL,
        customer_identifier character varying(160),
        applied_coupon_code character varying(40),
        delivery_fee_amount numeric(18,2) NOT NULL,
        delivery_fee_currency character varying(3) NOT NULL,
        items_subtotal_amount numeric(18,2) NOT NULL,
        items_subtotal_currency character varying(3) NOT NULL,
        items_discount_amount numeric(18,2) NOT NULL,
        items_discount_currency character varying(3) NOT NULL,
        delivery_discount_amount numeric(18,2) NOT NULL,
        delivery_discount_currency character varying(3) NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        total_currency character varying(3) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_shopping_carts" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    CREATE TABLE orders.shopping_cart_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        shopping_cart_id uuid NOT NULL,
        item_kind integer NOT NULL,
        product_id uuid,
        category_id uuid,
        variation_id uuid,
        acai_cup_size_id uuid,
        weighing_record_id uuid,
        item_title character varying(200) NOT NULL,
        customization_summary_text character varying(1000) NOT NULL,
        selections_payload_json character varying(4000) NOT NULL,
        customization_fingerprint character varying(64) NOT NULL,
        unit_price_amount numeric(18,2) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        quantity integer NOT NULL,
        line_subtotal_amount numeric(18,2) NOT NULL,
        line_subtotal_currency character varying(3) NOT NULL,
        has_non_combinable_promotion boolean NOT NULL,
        customer_notes character varying(400),
        added_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_shopping_cart_items" PRIMARY KEY (id),
        CONSTRAINT "FK_shopping_cart_items_shopping_carts_shopping_cart_id" FOREIGN KEY (shopping_cart_id) REFERENCES orders.shopping_carts (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    CREATE INDEX "IX_shopping_cart_items_shopping_cart_id" ON orders.shopping_cart_items (shopping_cart_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    CREATE INDEX "IX_shopping_cart_items_tenant_id_shopping_cart_id_customizatio~" ON orders.shopping_cart_items (tenant_id, shopping_cart_id, customization_fingerprint);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    CREATE UNIQUE INDEX "IX_shopping_carts_tenant_id_cart_session_key" ON orders.shopping_carts (tenant_id, cart_session_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    ALTER TABLE "orders"."shopping_carts" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "orders"."shopping_carts" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "orders"."shopping_carts";
    CREATE POLICY "pitako_tenant_isolation" ON "orders"."shopping_carts"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    ALTER TABLE "orders"."shopping_cart_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "orders"."shopping_cart_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "orders"."shopping_cart_items";
    CREATE POLICY "pitako_tenant_isolation" ON "orders"."shopping_cart_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125110505_InitialShoppingCartSchema') THEN
    INSERT INTO orders.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125110505_InitialShoppingCartSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE TABLE orders.orders (
        id uuid NOT NULL,
        store_unit_id uuid,
        order_number character varying(32) NOT NULL,
        channel integer NOT NULL,
        fulfillment_mode integer NOT NULL,
        status integer NOT NULL,
        customer_name character varying(160) NOT NULL,
        customer_identifier character varying(160),
        customer_phone character varying(40),
        delivery_address_summary character varying(400),
        applied_coupon_code character varying(40),
        items_subtotal_amount numeric(18,2) NOT NULL,
        items_subtotal_currency character varying(3) NOT NULL,
        items_discount_amount numeric(18,2) NOT NULL,
        items_discount_currency character varying(3) NOT NULL,
        delivery_fee_amount numeric(18,2) NOT NULL,
        delivery_fee_currency character varying(3) NOT NULL,
        delivery_discount_amount numeric(18,2) NOT NULL,
        delivery_discount_currency character varying(3) NOT NULL,
        net_delivery_fee_amount numeric(18,2) NOT NULL,
        net_delivery_fee_currency character varying(3) NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        total_currency character varying(3) NOT NULL,
        idempotency_key character varying(160),
        cancellation_reason character varying(300),
        placed_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        cancelled_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_orders" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE TABLE orders.order_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        order_id uuid NOT NULL,
        item_kind integer NOT NULL,
        product_id uuid,
        category_id uuid,
        variation_id uuid,
        acai_cup_size_id uuid,
        weighing_record_id uuid,
        item_title character varying(200) NOT NULL,
        customization_summary_text character varying(1000) NOT NULL,
        selections_payload_json character varying(4000) NOT NULL,
        unit_price_amount numeric(18,2) NOT NULL,
        unit_price_currency character varying(3) NOT NULL,
        quantity integer NOT NULL,
        line_subtotal_amount numeric(18,2) NOT NULL,
        line_subtotal_currency character varying(3) NOT NULL,
        customer_notes character varying(400),
        CONSTRAINT "PK_order_items" PRIMARY KEY (id),
        CONSTRAINT "FK_order_items_orders_order_id" FOREIGN KEY (order_id) REFERENCES orders.orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE TABLE orders.order_status_histories (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        order_id uuid NOT NULL,
        from_status integer,
        to_status integer NOT NULL,
        actor_user_id uuid,
        reason character varying(300) NOT NULL,
        changed_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_order_status_histories" PRIMARY KEY (id),
        CONSTRAINT "FK_order_status_histories_orders_order_id" FOREIGN KEY (order_id) REFERENCES orders.orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE INDEX "IX_order_items_order_id" ON orders.order_items (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE INDEX "IX_order_items_tenant_id_product_id" ON orders.order_items (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE INDEX "IX_order_status_histories_order_id" ON orders.order_status_histories (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE INDEX "IX_order_status_histories_tenant_id_order_id_changed_at_utc" ON orders.order_status_histories (tenant_id, order_id, changed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE UNIQUE INDEX "IX_orders_tenant_id_idempotency_key" ON orders.orders (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    CREATE INDEX "IX_orders_tenant_id_status_placed_at_utc" ON orders.orders (tenant_id, status, placed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    ALTER TABLE "orders"."orders" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "orders"."orders" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "orders"."orders";
    CREATE POLICY "pitako_tenant_isolation" ON "orders"."orders"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    ALTER TABLE "orders"."order_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "orders"."order_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "orders"."order_items";
    CREATE POLICY "pitako_tenant_isolation" ON "orders"."order_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    ALTER TABLE "orders"."order_status_histories" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "orders"."order_status_histories" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "orders"."order_status_histories";
    CREATE POLICY "pitako_tenant_isolation" ON "orders"."order_status_histories"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM orders.__ef_migrations_history WHERE "MigrationId" = '20261125111949_AddOrdersAndKanbanHistory') THEN
    INSERT INTO orders.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125111949_AddOrdersAndKanbanHistory', '8.0.11');
    END IF;
END $EF$;
COMMIT;

