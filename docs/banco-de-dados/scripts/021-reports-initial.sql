DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'reports') THEN
        CREATE SCHEMA reports;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS reports.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM reports.__ef_migrations_history WHERE "MigrationId" = '20261125124710_InitialReportsSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'reports') THEN
            CREATE SCHEMA reports;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM reports.__ef_migrations_history WHERE "MigrationId" = '20261125124710_InitialReportsSchema') THEN
    CREATE TABLE reports.report_export_audit_logs (
        id uuid NOT NULL,
        category integer NOT NULL,
        format integer NOT NULL,
        file_name character varying(200) NOT NULL,
        mime_type character varying(120) NOT NULL,
        row_count integer NOT NULL,
        file_size_bytes bigint NOT NULL,
        content_sha256 character varying(64) NOT NULL,
        period_start_utc timestamp with time zone NOT NULL,
        period_end_utc timestamp with time zone NOT NULL,
        exported_by_user_id uuid NOT NULL,
        exported_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_report_export_audit_logs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM reports.__ef_migrations_history WHERE "MigrationId" = '20261125124710_InitialReportsSchema') THEN
    CREATE INDEX "IX_report_export_audit_logs_tenant_id_category_exported_at_utc" ON reports.report_export_audit_logs (tenant_id, category, exported_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM reports.__ef_migrations_history WHERE "MigrationId" = '20261125124710_InitialReportsSchema') THEN
    ALTER TABLE "reports"."report_export_audit_logs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "reports"."report_export_audit_logs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "reports"."report_export_audit_logs";
    CREATE POLICY "pitako_tenant_isolation" ON "reports"."report_export_audit_logs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM reports.__ef_migrations_history WHERE "MigrationId" = '20261125124710_InitialReportsSchema') THEN
    INSERT INTO reports.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125124710_InitialReportsSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

