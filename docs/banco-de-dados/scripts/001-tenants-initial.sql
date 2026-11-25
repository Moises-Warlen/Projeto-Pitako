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

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.tenants ADD address character varying(350);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.tenants ADD contact_email character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.tenants ADD contact_phone character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.tenants ADD customer_account_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.store_units ADD address character varying(350);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.store_units ADD phone character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE tenants.store_units ADD time_zone_id character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    CREATE TABLE tenants.store_visual_identities (
        id uuid NOT NULL,
        draft_logo_url character varying(500),
        draft_cover_image_url character varying(500),
        draft_slogan character varying(200),
        draft_primary_color_hex character varying(7) NOT NULL,
        draft_secondary_color_hex character varying(7) NOT NULL,
        draft_accent_color_hex character varying(7) NOT NULL,
        draft_menu_layout_mode character varying(24) NOT NULL,
        published_logo_url character varying(500),
        published_cover_image_url character varying(500),
        published_slogan character varying(200),
        published_primary_color_hex character varying(7) NOT NULL,
        published_secondary_color_hex character varying(7) NOT NULL,
        published_accent_color_hex character varying(7) NOT NULL,
        published_menu_layout_mode character varying(24) NOT NULL,
        published_version integer NOT NULL,
        published_at_utc timestamp with time zone,
        has_unpublished_changes boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_store_visual_identities" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    CREATE TABLE tenants.store_banners (
        id uuid NOT NULL,
        visual_identity_id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        title character varying(160) NOT NULL,
        image_url character varying(500) NOT NULL,
        target_url character varying(500),
        display_order integer NOT NULL,
        is_draft boolean NOT NULL,
        CONSTRAINT "PK_store_banners" PRIMARY KEY (id),
        CONSTRAINT "FK_store_banners_store_visual_identities_visual_identity_id" FOREIGN KEY (visual_identity_id) REFERENCES tenants.store_visual_identities (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    CREATE INDEX "IX_tenants_customer_account_id" ON tenants.tenants (customer_account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    CREATE INDEX "IX_store_banners_visual_identity_id_is_draft_display_order" ON tenants.store_banners (visual_identity_id, is_draft, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    CREATE UNIQUE INDEX "IX_store_visual_identities_tenant_id" ON tenants.store_visual_identities (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE "tenants"."store_visual_identities" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "tenants"."store_visual_identities" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "tenants"."store_visual_identities";
    CREATE POLICY "pitako_tenant_isolation" ON "tenants"."store_visual_identities"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    ALTER TABLE "tenants"."store_banners" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "tenants"."store_banners" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "tenants"."store_banners";
    CREATE POLICY "pitako_tenant_isolation" ON "tenants"."store_banners"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM tenants.__ef_migrations_history WHERE "MigrationId" = '20261125012934_AddStoreBrandingAndSegmentFields') THEN
    INSERT INTO tenants.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125012934_AddStoreBrandingAndSegmentFields', '8.0.11');
    END IF;
END $EF$;
COMMIT;

