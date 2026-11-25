DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'pdv') THEN
        CREATE SCHEMA pdv;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS pdv.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'pdv') THEN
            CREATE SCHEMA pdv;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE TABLE pdv.cash_register_sessions (
        id uuid NOT NULL,
        store_unit_id uuid,
        terminal_code character varying(64) NOT NULL,
        opened_by_user_id uuid NOT NULL,
        opened_at_utc timestamp with time zone NOT NULL,
        status integer NOT NULL,
        opening_balance_amount numeric(18,2) NOT NULL,
        opening_balance_currency character varying(3) NOT NULL,
        expected_cash_amount numeric(18,2) NOT NULL,
        expected_cash_currency character varying(3) NOT NULL,
        counted_cash_amount numeric(18,2),
        counted_cash_currency character varying(3),
        cash_variance_amount numeric(18,2),
        cash_variance_currency character varying(3),
        closed_by_user_id uuid,
        closed_at_utc timestamp with time zone,
        closing_notes character varying(400),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_cash_register_sessions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE TABLE pdv.pdv_sales (
        id uuid NOT NULL,
        cash_register_session_id uuid NOT NULL,
        order_id uuid,
        receipt_number character varying(40) NOT NULL,
        gross_amount numeric(18,2) NOT NULL,
        gross_currency character varying(3) NOT NULL,
        discount_amount numeric(18,2) NOT NULL,
        discount_currency character varying(3) NOT NULL,
        discount_reason character varying(240),
        discount_authorized_by_user_id uuid,
        net_amount numeric(18,2) NOT NULL,
        net_currency character varying(3) NOT NULL,
        status integer NOT NULL,
        operator_user_id uuid NOT NULL,
        settled_at_utc timestamp with time zone NOT NULL,
        idempotency_key character varying(160),
        cancellation_reason character varying(300),
        cancelled_by_user_id uuid,
        cancelled_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_pdv_sales" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE TABLE pdv.cash_movements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        cash_register_session_id uuid NOT NULL,
        movement_type integer NOT NULL,
        payment_method integer NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        reason character varying(300) NOT NULL,
        pdv_sale_id uuid,
        order_id uuid,
        actor_user_id uuid NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_cash_movements" PRIMARY KEY (id),
        CONSTRAINT "FK_cash_movements_cash_register_sessions_cash_register_session~" FOREIGN KEY (cash_register_session_id) REFERENCES pdv.cash_register_sessions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE TABLE pdv.pdv_sale_payments (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        pdv_sale_id uuid NOT NULL,
        method integer NOT NULL,
        allocated_amount numeric(18,2) NOT NULL,
        allocated_currency character varying(3) NOT NULL,
        tendered_amount numeric(18,2) NOT NULL,
        tendered_currency character varying(3) NOT NULL,
        change_amount numeric(18,2) NOT NULL,
        change_currency character varying(3) NOT NULL,
        authorization_code character varying(80),
        CONSTRAINT "PK_pdv_sale_payments" PRIMARY KEY (id),
        CONSTRAINT "FK_pdv_sale_payments_pdv_sales_pdv_sale_id" FOREIGN KEY (pdv_sale_id) REFERENCES pdv.pdv_sales (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_cash_movements_cash_register_session_id" ON pdv.cash_movements (cash_register_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_cash_movements_tenant_id_cash_register_session_id_occurred_~" ON pdv.cash_movements (tenant_id, cash_register_session_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_cash_register_sessions_tenant_id_terminal_code_status" ON pdv.cash_register_sessions (tenant_id, terminal_code, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_pdv_sale_payments_pdv_sale_id" ON pdv.pdv_sale_payments (pdv_sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_pdv_sale_payments_tenant_id_pdv_sale_id" ON pdv.pdv_sale_payments (tenant_id, pdv_sale_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE INDEX "IX_pdv_sales_tenant_id_cash_register_session_id_settled_at_utc" ON pdv.pdv_sales (tenant_id, cash_register_session_id, settled_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    CREATE UNIQUE INDEX "IX_pdv_sales_tenant_id_idempotency_key" ON pdv.pdv_sales (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    ALTER TABLE "pdv"."cash_register_sessions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "pdv"."cash_register_sessions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "pdv"."cash_register_sessions";
    CREATE POLICY "pitako_tenant_isolation" ON "pdv"."cash_register_sessions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    ALTER TABLE "pdv"."cash_movements" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "pdv"."cash_movements" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "pdv"."cash_movements";
    CREATE POLICY "pitako_tenant_isolation" ON "pdv"."cash_movements"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    ALTER TABLE "pdv"."pdv_sales" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "pdv"."pdv_sales" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "pdv"."pdv_sales";
    CREATE POLICY "pitako_tenant_isolation" ON "pdv"."pdv_sales"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    ALTER TABLE "pdv"."pdv_sale_payments" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "pdv"."pdv_sale_payments" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "pdv"."pdv_sale_payments";
    CREATE POLICY "pitako_tenant_isolation" ON "pdv"."pdv_sale_payments"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pdv.__ef_migrations_history WHERE "MigrationId" = '20261125112738_InitialPdvCashierSchema') THEN
    INSERT INTO pdv.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125112738_InitialPdvCashierSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

