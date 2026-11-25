DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenants') THEN
        CREATE SCHEMA tenants;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS tenants.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenants') THEN
            CREATE SCHEMA tenants;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    CREATE TABLE tenants.store_units (
        id uuid NOT NULL,
        name character varying(160) NOT NULL,
        code character varying(40) NOT NULL,
        is_headquarters boolean NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_store_units" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    CREATE TABLE tenants.tenants (
        id uuid NOT NULL,
        legal_name character varying(200) NOT NULL,
        trade_name character varying(200) NOT NULL,
        slug character varying(100) NOT NULL,
        cnpj character varying(14) NOT NULL,
        segment_code character varying(60) NOT NULL,
        status integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    CREATE UNIQUE INDEX "IX_store_units_tenant_id_code" ON tenants.store_units (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    CREATE UNIQUE INDEX "IX_tenants_cnpj" ON tenants.tenants (cnpj);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    CREATE UNIQUE INDEX "IX_tenants_slug" ON tenants.tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    ALTER TABLE "tenants"."store_units" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "tenants"."store_units" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "tenants"."store_units";
    CREATE POLICY "pitako_tenant_isolation" ON "tenants"."store_units"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125005617_InitialTenantsSchema') THEN
    INSERT INTO tenants.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125005617_InitialTenantsSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

