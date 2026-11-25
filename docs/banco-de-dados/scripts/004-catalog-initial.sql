DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'catalog') THEN
        CREATE SCHEMA catalog;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS catalog.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'catalog') THEN
            CREATE SCHEMA catalog;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.categories (
        id uuid NOT NULL,
        name character varying(140) NOT NULL,
        description character varying(500),
        image_url character varying(500),
        display_order integer NOT NULL,
        status integer NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_categories" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.combos (
        id uuid NOT NULL,
        name character varying(180) NOT NULL,
        description character varying(800),
        combo_price_amount numeric(18,2) NOT NULL,
        combo_price_currency character varying(3) NOT NULL,
        primary_image_url character varying(500),
        status integer NOT NULL,
        is_featured boolean NOT NULL,
        display_order integer NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_combos" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.modifier_groups (
        id uuid NOT NULL,
        name character varying(140) NOT NULL,
        type integer NOT NULL,
        min_selections integer NOT NULL,
        max_selections integer NOT NULL,
        is_required boolean NOT NULL,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_modifier_groups" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.products (
        id uuid NOT NULL,
        category_id uuid NOT NULL,
        name character varying(180) NOT NULL,
        description character varying(1000),
        sku character varying(64),
        base_price_amount numeric(18,2) NOT NULL,
        base_price_currency character varying(3) NOT NULL,
        sale_mode integer NOT NULL,
        status integer NOT NULL,
        is_featured boolean NOT NULL,
        display_order integer NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_products" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.promotions (
        id uuid NOT NULL,
        name character varying(160) NOT NULL,
        discount_type integer NOT NULL,
        discount_value numeric(18,2) NOT NULL,
        starts_at_utc timestamp with time zone NOT NULL,
        ends_at_utc timestamp with time zone NOT NULL,
        is_active boolean NOT NULL,
        can_combine_with_coupons boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_promotions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.combo_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        combo_id uuid NOT NULL,
        product_id uuid NOT NULL,
        quantity integer NOT NULL,
        variation_id uuid,
        notes character varying(300),
        CONSTRAINT "PK_combo_items" PRIMARY KEY (id),
        CONSTRAINT "FK_combo_items_combos_combo_id" FOREIGN KEY (combo_id) REFERENCES catalog.combos (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.modifier_options (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        modifier_group_id uuid NOT NULL,
        name character varying(140) NOT NULL,
        additional_price_amount numeric(18,2) NOT NULL,
        additional_price_currency character varying(3) NOT NULL,
        max_quantity integer NOT NULL,
        display_order integer NOT NULL,
        is_available boolean NOT NULL,
        CONSTRAINT "PK_modifier_options" PRIMARY KEY (id),
        CONSTRAINT "FK_modifier_options_modifier_groups_modifier_group_id" FOREIGN KEY (modifier_group_id) REFERENCES catalog.modifier_groups (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.product_images (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        product_id uuid NOT NULL,
        image_url character varying(500) NOT NULL,
        display_order integer NOT NULL,
        is_primary boolean NOT NULL,
        CONSTRAINT "PK_product_images" PRIMARY KEY (id),
        CONSTRAINT "FK_product_images_products_product_id" FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.product_modifier_links (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        product_id uuid NOT NULL,
        modifier_group_id uuid NOT NULL,
        display_order integer NOT NULL,
        CONSTRAINT "PK_product_modifier_links" PRIMARY KEY (id),
        CONSTRAINT "FK_product_modifier_links_products_product_id" FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.product_variations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        product_id uuid NOT NULL,
        name character varying(140) NOT NULL,
        sku character varying(64),
        price_amount numeric(18,2) NOT NULL,
        price_currency character varying(3) NOT NULL,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        CONSTRAINT "PK_product_variations" PRIMARY KEY (id),
        CONSTRAINT "FK_product_variations_products_product_id" FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE TABLE catalog.promotion_targets (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        promotion_id uuid NOT NULL,
        target_type integer NOT NULL,
        target_id uuid NOT NULL,
        CONSTRAINT "PK_promotion_targets" PRIMARY KEY (id),
        CONSTRAINT "FK_promotion_targets_promotions_promotion_id" FOREIGN KEY (promotion_id) REFERENCES catalog.promotions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_categories_tenant_id_display_order" ON catalog.categories (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_combo_items_combo_id" ON catalog.combo_items (combo_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_combo_items_tenant_id_combo_id" ON catalog.combo_items (tenant_id, combo_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_combos_tenant_id_display_order" ON catalog.combos (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_modifier_groups_tenant_id_display_order" ON catalog.modifier_groups (tenant_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_modifier_options_modifier_group_id" ON catalog.modifier_options (modifier_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_modifier_options_tenant_id_modifier_group_id_display_order" ON catalog.modifier_options (tenant_id, modifier_group_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_product_images_product_id" ON catalog.product_images (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_product_images_tenant_id_product_id_display_order" ON catalog.product_images (tenant_id, product_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_product_modifier_links_product_id" ON catalog.product_modifier_links (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE UNIQUE INDEX "IX_product_modifier_links_tenant_id_product_id_modifier_group_~" ON catalog.product_modifier_links (tenant_id, product_id, modifier_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_product_variations_product_id" ON catalog.product_variations (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_product_variations_tenant_id_product_id_display_order" ON catalog.product_variations (tenant_id, product_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_products_tenant_id_category_id_display_order" ON catalog.products (tenant_id, category_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_promotion_targets_promotion_id" ON catalog.promotion_targets (promotion_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE UNIQUE INDEX "IX_promotion_targets_tenant_id_promotion_id_target_type_target~" ON catalog.promotion_targets (tenant_id, promotion_id, target_type, target_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    CREATE INDEX "IX_promotions_tenant_id_is_active" ON catalog.promotions (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."categories" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."categories" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."categories";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."categories"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."products" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."products" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."products";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."products"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."product_images" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."product_images" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."product_images";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."product_images"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."product_variations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."product_variations" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."product_variations";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."product_variations"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."product_modifier_links" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."product_modifier_links" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."product_modifier_links";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."product_modifier_links"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."modifier_groups" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."modifier_groups" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."modifier_groups";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."modifier_groups"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."modifier_options" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."modifier_options" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."modifier_options";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."modifier_options"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."combos" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."combos" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."combos";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."combos"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."combo_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."combo_items" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."combo_items";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."combo_items"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."promotions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."promotions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."promotions";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."promotions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    ALTER TABLE "catalog"."promotion_targets" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "catalog"."promotion_targets" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "catalog"."promotion_targets";
    CREATE POLICY "pitako_tenant_isolation" ON "catalog"."promotion_targets"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM catalog.__ef_migrations_history WHERE "MigrationId" = '20261125013539_InitialCatalogSchema') THEN
    INSERT INTO catalog.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125013539_InitialCatalogSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

