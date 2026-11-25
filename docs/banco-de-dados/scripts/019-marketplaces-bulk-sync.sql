DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'marketplaces') THEN
        CREATE SCHEMA marketplaces;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS marketplaces.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'marketplaces') THEN
            CREATE SCHEMA marketplaces;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE TABLE marketplaces.catalog_mappings (
        id uuid NOT NULL,
        store_binding_id uuid NOT NULL,
        provider integer NOT NULL,
        internal_product_id uuid NOT NULL,
        internal_item_title character varying(200) NOT NULL,
        external_sku_code character varying(120) NOT NULL,
        is_paused_at_partner boolean NOT NULL,
        last_synced_price_amount numeric(18,2) NOT NULL,
        last_synced_price_currency character varying(3) NOT NULL,
        last_synced_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_catalog_mappings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE TABLE marketplaces.inbound_events (
        id uuid NOT NULL,
        provider integer NOT NULL,
        store_binding_id uuid,
        external_event_id character varying(120) NOT NULL,
        external_order_id character varying(120) NOT NULL,
        external_merchant_id character varying(120) NOT NULL,
        event_type character varying(80) NOT NULL,
        raw_payload_json character varying(16000) NOT NULL,
        signature_verified boolean NOT NULL,
        sync_status integer NOT NULL,
        normalized_customer_name character varying(160),
        normalized_fulfillment integer,
        normalized_total_amount numeric(18,2) NOT NULL,
        normalized_total_currency character varying(3) NOT NULL,
        normalized_items_json character varying(12000) NOT NULL,
        internal_order_id uuid,
        failure_reason character varying(500),
        received_at_utc timestamp with time zone NOT NULL,
        processed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_inbound_events" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE TABLE marketplaces.partner_accounts (
        id uuid NOT NULL,
        provider integer NOT NULL,
        auth_kind integer NOT NULL,
        account_label character varying(160) NOT NULL,
        client_or_merchant_identifier character varying(160) NOT NULL,
        encrypted_credentials_blob character varying(4000) NOT NULL,
        is_active boolean NOT NULL,
        last_health_check_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_partner_accounts" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE TABLE marketplaces.store_bindings (
        id uuid NOT NULL,
        partner_account_id uuid NOT NULL,
        provider integer NOT NULL,
        store_unit_id uuid,
        external_merchant_id character varying(120) NOT NULL,
        external_store_name character varying(160) NOT NULL,
        status integer NOT NULL,
        auto_accept_orders boolean NOT NULL,
        last_sync_utc timestamp with time zone,
        last_error character varying(500),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_store_bindings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE UNIQUE INDEX "IX_catalog_mappings_tenant_id_store_binding_id_internal_produc~" ON marketplaces.catalog_mappings (tenant_id, store_binding_id, internal_product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE UNIQUE INDEX "IX_inbound_events_tenant_id_provider_external_event_id" ON marketplaces.inbound_events (tenant_id, provider, external_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE INDEX "IX_partner_accounts_tenant_id_provider_client_or_merchant_iden~" ON marketplaces.partner_accounts (tenant_id, provider, client_or_merchant_identifier);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    CREATE UNIQUE INDEX "IX_store_bindings_tenant_id_provider_external_merchant_id" ON marketplaces.store_bindings (tenant_id, provider, external_merchant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    ALTER TABLE "marketplaces"."partner_accounts" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."partner_accounts" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."partner_accounts";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."partner_accounts"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    ALTER TABLE "marketplaces"."store_bindings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."store_bindings" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."store_bindings";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."store_bindings"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    ALTER TABLE "marketplaces"."inbound_events" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."inbound_events" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."inbound_events";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."inbound_events"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    ALTER TABLE "marketplaces"."catalog_mappings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."catalog_mappings" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."catalog_mappings";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."catalog_mappings"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122047_InitialMarketplacesSchema') THEN
    INSERT INTO marketplaces.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125122047_InitialMarketplacesSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    CREATE TABLE marketplaces.bulk_jobs (
        id uuid NOT NULL,
        operation_type integer NOT NULL,
        operation_summary character varying(200) NOT NULL,
        internal_product_id uuid,
        target_price_amount numeric(18,2),
        target_paused_state boolean,
        requested_by_user_id uuid NOT NULL,
        status integer NOT NULL,
        total_targets integer NOT NULL,
        succeeded_count integer NOT NULL,
        failed_count integer NOT NULL,
        started_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_bulk_jobs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    CREATE TABLE marketplaces.bulk_job_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        bulk_job_id uuid NOT NULL,
        store_binding_id uuid NOT NULL,
        provider integer NOT NULL,
        external_merchant_id character varying(120) NOT NULL,
        external_store_name character varying(160) NOT NULL,
        target_sku_or_ref character varying(120),
        outcome integer NOT NULL,
        error_code character varying(80),
        error_message character varying(500),
        attempt_count integer NOT NULL,
        executed_at_utc timestamp with time zone,
        CONSTRAINT "PK_bulk_job_items" PRIMARY KEY (id),
        CONSTRAINT "FK_bulk_job_items_bulk_jobs_bulk_job_id" FOREIGN KEY (bulk_job_id) REFERENCES marketplaces.bulk_jobs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    CREATE INDEX "IX_bulk_job_items_bulk_job_id" ON marketplaces.bulk_job_items (bulk_job_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    CREATE INDEX "IX_bulk_job_items_tenant_id_bulk_job_id" ON marketplaces.bulk_job_items (tenant_id, bulk_job_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    CREATE INDEX "IX_bulk_jobs_tenant_id_status_started_at_utc" ON marketplaces.bulk_jobs (tenant_id, status, started_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    ALTER TABLE "marketplaces"."bulk_jobs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."bulk_jobs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."bulk_jobs";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."bulk_jobs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    ALTER TABLE "marketplaces"."bulk_job_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "marketplaces"."bulk_job_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "marketplaces"."bulk_job_items";
    CREATE POLICY "pitako_tenant_isolation" ON "marketplaces"."bulk_job_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM marketplaces.__ef_migrations_history WHERE "MigrationId" = '20261125122915_AddMarketplaceBulkJobs') THEN
    INSERT INTO marketplaces.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125122915_AddMarketplaceBulkJobs', '8.0.11');
    END IF;
END $EF$;
COMMIT;

