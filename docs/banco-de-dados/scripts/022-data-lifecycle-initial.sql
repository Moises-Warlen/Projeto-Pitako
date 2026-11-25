DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'data_lifecycle') THEN
        CREATE SCHEMA data_lifecycle;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS data_lifecycle.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'data_lifecycle') THEN
            CREATE SCHEMA data_lifecycle;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE TABLE data_lifecycle.lgpd_data_requests (
        id uuid NOT NULL,
        request_type integer NOT NULL,
        status integer NOT NULL,
        subject_hash character varying(64) NOT NULL,
        subject_masked character varying(80) NOT NULL,
        reason_or_notes character varying(300) NOT NULL,
        exported_portability_json character varying(32000),
        anonymized_records_count integer NOT NULL,
        processed_by_user_id uuid NOT NULL,
        processed_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_lgpd_data_requests" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE TABLE data_lifecycle.migration_jobs (
        id uuid NOT NULL,
        source_system integer NOT NULL,
        is_dry_run boolean NOT NULL,
        status integer NOT NULL,
        total_rows integer NOT NULL,
        imported_count integer NOT NULL,
        skipped_duplicate_count integer NOT NULL,
        failed_count integer NOT NULL,
        normalized_items_json character varying(32000) NOT NULL,
        validation_errors_json character varying(16000) NOT NULL,
        requested_by_user_id uuid NOT NULL,
        executed_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_migration_jobs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE TABLE data_lifecycle.tenant_backup_snapshots (
        id uuid NOT NULL,
        backup_code character varying(32) NOT NULL,
        encrypted_bundle_base64 character varying(64000) NOT NULL,
        plain_bundle_sha256 character varying(64) NOT NULL,
        size_bytes bigint NOT NULL,
        includes_catalog boolean NOT NULL,
        includes_orders boolean NOT NULL,
        includes_customers boolean NOT NULL,
        status integer NOT NULL,
        created_by_user_id uuid NOT NULL,
        backup_created_at_utc timestamp with time zone NOT NULL,
        restored_by_user_id uuid,
        restored_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_tenant_backup_snapshots" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE INDEX "IX_lgpd_data_requests_tenant_id_subject_hash_processed_at_utc" ON data_lifecycle.lgpd_data_requests (tenant_id, subject_hash, processed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE INDEX "IX_migration_jobs_tenant_id_source_system_executed_at_utc" ON data_lifecycle.migration_jobs (tenant_id, source_system, executed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    CREATE UNIQUE INDEX "IX_tenant_backup_snapshots_tenant_id_backup_code" ON data_lifecycle.tenant_backup_snapshots (tenant_id, backup_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    ALTER TABLE "data_lifecycle"."migration_jobs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "data_lifecycle"."migration_jobs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "data_lifecycle"."migration_jobs";
    CREATE POLICY "pitako_tenant_isolation" ON "data_lifecycle"."migration_jobs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    ALTER TABLE "data_lifecycle"."tenant_backup_snapshots" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "data_lifecycle"."tenant_backup_snapshots" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "data_lifecycle"."tenant_backup_snapshots";
    CREATE POLICY "pitako_tenant_isolation" ON "data_lifecycle"."tenant_backup_snapshots"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    ALTER TABLE "data_lifecycle"."lgpd_data_requests" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "data_lifecycle"."lgpd_data_requests" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "data_lifecycle"."lgpd_data_requests";
    CREATE POLICY "pitako_tenant_isolation" ON "data_lifecycle"."lgpd_data_requests"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM data_lifecycle.__ef_migrations_history WHERE "MigrationId" = '20261125125709_InitialDataLifecycleSchema') THEN
    INSERT INTO data_lifecycle.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125125709_InitialDataLifecycleSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

