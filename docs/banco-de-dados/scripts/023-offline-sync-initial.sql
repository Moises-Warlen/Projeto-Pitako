DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'offline_sync') THEN
        CREATE SCHEMA offline_sync;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS offline_sync.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'offline_sync') THEN
            CREATE SCHEMA offline_sync;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    CREATE TABLE offline_sync.offline_terminals (
        id uuid NOT NULL,
        store_unit_id uuid,
        terminal_code character varying(40) NOT NULL,
        device_fingerprint_hash character varying(64) NOT NULL,
        catalog_snapshot_version integer NOT NULL,
        max_offline_hours integer NOT NULL,
        is_active boolean NOT NULL,
        last_sync_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_offline_terminals" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    CREATE TABLE offline_sync.sync_operations (
        id uuid NOT NULL,
        offline_terminal_id uuid NOT NULL,
        idempotency_key character varying(120) NOT NULL,
        client_sequence_number bigint NOT NULL,
        operation_type integer NOT NULL,
        entity_reference_key character varying(120) NOT NULL,
        payload_json character varying(16000) NOT NULL,
        payload_sha256 character varying(64) NOT NULL,
        catalog_snapshot_version_used integer NOT NULL,
        client_occurred_at_utc timestamp with time zone NOT NULL,
        sync_status integer NOT NULL,
        server_entity_id uuid,
        conflict_details character varying(500),
        resolved_by_user_id uuid,
        resolution_notes character varying(300),
        synced_at_utc timestamp with time zone,
        resolved_at_utc timestamp with time zone,
        retry_count integer NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_sync_operations" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    CREATE UNIQUE INDEX "IX_offline_terminals_tenant_id_terminal_code" ON offline_sync.offline_terminals (tenant_id, terminal_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    CREATE UNIQUE INDEX "IX_sync_operations_tenant_id_offline_terminal_id_idempotency_k~" ON offline_sync.sync_operations (tenant_id, offline_terminal_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    CREATE INDEX "IX_sync_operations_tenant_id_sync_status_client_occurred_at_utc" ON offline_sync.sync_operations (tenant_id, sync_status, client_occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    ALTER TABLE "offline_sync"."offline_terminals" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "offline_sync"."offline_terminals" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "offline_sync"."offline_terminals";
    CREATE POLICY "pitako_tenant_isolation" ON "offline_sync"."offline_terminals"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    ALTER TABLE "offline_sync"."sync_operations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "offline_sync"."sync_operations" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "offline_sync"."sync_operations";
    CREATE POLICY "pitako_tenant_isolation" ON "offline_sync"."sync_operations"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM offline_sync.__ef_migrations_history WHERE "MigrationId" = '20261125130641_InitialOfflineSyncSchema') THEN
    INSERT INTO offline_sync.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125130641_InitialOfflineSyncSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

