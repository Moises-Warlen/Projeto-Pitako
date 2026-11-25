DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'master') THEN
        CREATE SCHEMA master;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS master.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'master') THEN
            CREATE SCHEMA master;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.commercial_plans (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(160) NOT NULL,
        description character varying(600) NOT NULL,
        price_amount numeric(18,2) NOT NULL,
        price_currency character varying(3) NOT NULL,
        billing_cycle integer NOT NULL,
        trial_days integer NOT NULL,
        is_active boolean NOT NULL,
        is_public boolean NOT NULL,
        establishments_is_unlimited boolean NOT NULL,
        establishments_max_allowed integer,
        units_is_unlimited boolean NOT NULL,
        units_max_allowed integer,
        marketplace_stores_is_unlimited boolean NOT NULL,
        marketplace_stores_max_allowed integer,
        users_is_unlimited boolean NOT NULL,
        users_max_allowed integer,
        devices_is_unlimited boolean NOT NULL,
        devices_max_allowed integer,
        concurrent_sessions_is_unlimited boolean NOT NULL,
        concurrent_sessions_max_allowed integer,
        monthly_orders_is_unlimited boolean NOT NULL,
        monthly_orders_max_allowed integer,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_commercial_plans" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.customer_accounts (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        document_number character varying(14) NOT NULL,
        billing_email character varying(256) NOT NULL,
        phone character varying(32),
        status integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_customer_accounts" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.global_settings (
        id uuid NOT NULL,
        key character varying(120) NOT NULL,
        value character varying(2000) NOT NULL,
        description character varying(400),
        is_secret boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_global_settings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.subscriptions (
        id uuid NOT NULL,
        customer_account_id uuid NOT NULL,
        plan_id uuid NOT NULL,
        status integer NOT NULL,
        started_at_utc timestamp with time zone NOT NULL,
        trial_ends_at_utc timestamp with time zone,
        current_period_start_utc timestamp with time zone NOT NULL,
        current_period_end_utc timestamp with time zone NOT NULL,
        suspended_at_utc timestamp with time zone,
        suspension_reason character varying(300),
        cancelled_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_subscriptions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.support_tickets (
        id uuid NOT NULL,
        customer_account_id uuid NOT NULL,
        tenant_id uuid,
        subject character varying(200) NOT NULL,
        description character varying(2000) NOT NULL,
        priority integer NOT NULL,
        status integer NOT NULL,
        assigned_to_saas_user_id uuid,
        resolution_notes character varying(2000),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        resolved_at_utc timestamp with time zone,
        CONSTRAINT "PK_support_tickets" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.plan_features (
        id uuid NOT NULL,
        plan_id uuid NOT NULL,
        feature_code character varying(64) NOT NULL,
        CONSTRAINT "PK_plan_features" PRIMARY KEY (id),
        CONSTRAINT "FK_plan_features_commercial_plans_plan_id" FOREIGN KEY (plan_id) REFERENCES master.commercial_plans (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.plan_modules (
        id uuid NOT NULL,
        plan_id uuid NOT NULL,
        module_code character varying(64) NOT NULL,
        CONSTRAINT "PK_plan_modules" PRIMARY KEY (id),
        CONSTRAINT "FK_plan_modules_commercial_plans_plan_id" FOREIGN KEY (plan_id) REFERENCES master.commercial_plans (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE TABLE master.subscription_invoices (
        id uuid NOT NULL,
        subscription_id uuid NOT NULL,
        reference_period character varying(40) NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        due_date_utc timestamp with time zone NOT NULL,
        status integer NOT NULL,
        paid_at_utc timestamp with time zone,
        payment_gateway_reference character varying(128),
        payment_method character varying(64),
        CONSTRAINT "PK_subscription_invoices" PRIMARY KEY (id),
        CONSTRAINT "FK_subscription_invoices_subscriptions_subscription_id" FOREIGN KEY (subscription_id) REFERENCES master.subscriptions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE UNIQUE INDEX "IX_commercial_plans_code" ON master.commercial_plans (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE INDEX "IX_customer_accounts_document_number" ON master.customer_accounts (document_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE UNIQUE INDEX "IX_global_settings_key" ON master.global_settings (key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE UNIQUE INDEX "IX_plan_features_plan_id_feature_code" ON master.plan_features (plan_id, feature_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE UNIQUE INDEX "IX_plan_modules_plan_id_module_code" ON master.plan_modules (plan_id, module_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE INDEX "IX_subscription_invoices_subscription_id" ON master.subscription_invoices (subscription_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    CREATE INDEX "IX_subscriptions_customer_account_id_status" ON master.subscriptions (customer_account_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM master.__ef_migrations_history WHERE "MigrationId" = '20261125011248_InitialMasterPanelSchema') THEN
    INSERT INTO master.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125011248_InitialMasterPanelSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

