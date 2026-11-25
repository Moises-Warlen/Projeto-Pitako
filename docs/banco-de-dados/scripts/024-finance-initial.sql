DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'finance') THEN
        CREATE SCHEMA finance;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS finance.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'finance') THEN
            CREATE SCHEMA finance;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.cash_flow_ledger (
        id uuid NOT NULL,
        store_unit_id uuid,
        financial_entry_id uuid,
        direction integer NOT NULL,
        description character varying(300) NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        payment_method_name character varying(60) NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_cash_flow_ledger" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.financial_entries (
        id uuid NOT NULL,
        store_unit_id uuid,
        entry_code character varying(32) NOT NULL,
        kind integer NOT NULL,
        dre_category integer NOT NULL,
        counterparty_name character varying(160) NOT NULL,
        description character varying(300) NOT NULL,
        reference_document character varying(80),
        original_amount numeric(18,2) NOT NULL,
        original_currency character varying(3) NOT NULL,
        fee_adjustment_amount numeric(18,2) NOT NULL,
        fee_adjustment_currency character varying(3) NOT NULL,
        settled_amount numeric(18,2) NOT NULL,
        settled_currency character varying(3) NOT NULL,
        due_date_utc timestamp with time zone NOT NULL,
        status integer NOT NULL,
        settled_at_utc timestamp with time zone,
        payment_method_name character varying(60),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_financial_entries" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.payment_gateway_configs (
        id uuid NOT NULL,
        provider integer NOT NULL,
        merchant_account_code character varying(120) NOT NULL,
        encrypted_secret_blob character varying(4000) NOT NULL,
        default_mdr_percent numeric(5,2) NOT NULL,
        settlement_days integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_payment_gateway_configs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.payment_transactions (
        id uuid NOT NULL,
        gateway_config_id uuid NOT NULL,
        provider integer NOT NULL,
        order_or_sale_reference character varying(80) NOT NULL,
        external_transaction_id character varying(120) NOT NULL,
        nsu_code character varying(64),
        card_brand character varying(40),
        card_last4_digits character varying(4),
        gross_amount numeric(18,2) NOT NULL,
        gross_currency character varying(3) NOT NULL,
        gateway_fee_amount numeric(18,2) NOT NULL,
        gateway_fee_currency character varying(3) NOT NULL,
        net_amount numeric(18,2) NOT NULL,
        net_currency character varying(3) NOT NULL,
        status integer NOT NULL,
        failure_reason character varying(400),
        processed_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_payment_transactions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.reconciliation_batches (
        id uuid NOT NULL,
        batch_code character varying(32) NOT NULL,
        provider_name character varying(120) NOT NULL,
        executed_at_utc timestamp with time zone NOT NULL,
        exact_matches_count integer NOT NULL,
        divergences_count integer NOT NULL,
        total_variance_amount numeric(18,2) NOT NULL,
        total_variance_currency character varying(3) NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_reconciliation_batches" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE TABLE finance.reconciliation_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        batch_id uuid NOT NULL,
        external_transaction_ref character varying(120) NOT NULL,
        internal_net_amount numeric(18,2) NOT NULL,
        internal_net_currency character varying(3) NOT NULL,
        statement_net_amount numeric(18,2) NOT NULL,
        statement_net_currency character varying(3) NOT NULL,
        variance_amount numeric(18,2) NOT NULL,
        variance_currency character varying(3) NOT NULL,
        match_status integer NOT NULL,
        notes character varying(300),
        CONSTRAINT "PK_reconciliation_lines" PRIMARY KEY (id),
        CONSTRAINT "FK_reconciliation_lines_reconciliation_batches_batch_id" FOREIGN KEY (batch_id) REFERENCES finance.reconciliation_batches (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE INDEX "IX_cash_flow_ledger_tenant_id_occurred_at_utc" ON finance.cash_flow_ledger (tenant_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE UNIQUE INDEX "IX_financial_entries_tenant_id_entry_code" ON finance.financial_entries (tenant_id, entry_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE INDEX "IX_financial_entries_tenant_id_kind_due_date_utc" ON finance.financial_entries (tenant_id, kind, due_date_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE UNIQUE INDEX "IX_payment_gateway_configs_tenant_id_provider_merchant_account~" ON finance.payment_gateway_configs (tenant_id, provider, merchant_account_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE UNIQUE INDEX "IX_payment_transactions_tenant_id_external_transaction_id" ON finance.payment_transactions (tenant_id, external_transaction_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE UNIQUE INDEX "IX_reconciliation_batches_tenant_id_batch_code" ON finance.reconciliation_batches (tenant_id, batch_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE INDEX "IX_reconciliation_lines_batch_id" ON finance.reconciliation_lines (batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    CREATE INDEX "IX_reconciliation_lines_tenant_id_batch_id" ON finance.reconciliation_lines (tenant_id, batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."financial_entries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."financial_entries" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."financial_entries";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."financial_entries"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."cash_flow_ledger" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."cash_flow_ledger" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."cash_flow_ledger";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."cash_flow_ledger"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."payment_gateway_configs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."payment_gateway_configs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."payment_gateway_configs";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."payment_gateway_configs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."payment_transactions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."payment_transactions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."payment_transactions";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."payment_transactions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."reconciliation_batches" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."reconciliation_batches" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."reconciliation_batches";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."reconciliation_batches"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    ALTER TABLE "finance"."reconciliation_lines" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "finance"."reconciliation_lines" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "finance"."reconciliation_lines";
    CREATE POLICY "pitako_tenant_isolation" ON "finance"."reconciliation_lines"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM finance.__ef_migrations_history WHERE "MigrationId" = '20261125131725_InitialFinanceSchema') THEN
    INSERT INTO finance.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125131725_InitialFinanceSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

