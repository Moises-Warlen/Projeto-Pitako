DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'coupons') THEN
        CREATE SCHEMA coupons;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS coupons.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'coupons') THEN
            CREATE SCHEMA coupons;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE TABLE coupons.coupons (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        description character varying(240),
        discount_type integer NOT NULL,
        discount_value numeric(18,2) NOT NULL,
        max_discount_amount numeric(18,2),
        max_discount_currency character varying(3),
        minimum_order_amount numeric(18,2) NOT NULL,
        minimum_order_currency character varying(3) NOT NULL,
        valid_from_utc timestamp with time zone NOT NULL,
        valid_until_utc timestamp with time zone NOT NULL,
        max_total_uses integer,
        max_uses_per_customer integer,
        can_combine_with_promotions boolean NOT NULL,
        is_active boolean NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_coupons" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE TABLE coupons.coupon_eligibility_rules (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        coupon_id uuid NOT NULL,
        target_type integer NOT NULL,
        target_id uuid NOT NULL,
        CONSTRAINT "PK_coupon_eligibility_rules" PRIMARY KEY (id),
        CONSTRAINT "FK_coupon_eligibility_rules_coupons_coupon_id" FOREIGN KEY (coupon_id) REFERENCES coupons.coupons (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE TABLE coupons.coupon_redemptions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        coupon_id uuid NOT NULL,
        order_id uuid NOT NULL,
        customer_identifier character varying(160) NOT NULL,
        discount_applied_amount numeric(18,2) NOT NULL,
        discount_applied_currency character varying(3) NOT NULL,
        redeemed_at_utc timestamp with time zone NOT NULL,
        reversed_at_utc timestamp with time zone,
        CONSTRAINT "PK_coupon_redemptions" PRIMARY KEY (id),
        CONSTRAINT "FK_coupon_redemptions_coupons_coupon_id" FOREIGN KEY (coupon_id) REFERENCES coupons.coupons (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE INDEX "IX_coupon_eligibility_rules_coupon_id" ON coupons.coupon_eligibility_rules (coupon_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE INDEX "IX_coupon_eligibility_rules_tenant_id_coupon_id" ON coupons.coupon_eligibility_rules (tenant_id, coupon_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE INDEX "IX_coupon_redemptions_coupon_id" ON coupons.coupon_redemptions (coupon_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE INDEX "IX_coupon_redemptions_tenant_id_coupon_id_customer_identifier" ON coupons.coupon_redemptions (tenant_id, coupon_id, customer_identifier);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE INDEX "IX_coupon_redemptions_tenant_id_coupon_id_order_id" ON coupons.coupon_redemptions (tenant_id, coupon_id, order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    CREATE UNIQUE INDEX "IX_coupons_tenant_id_code" ON coupons.coupons (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    ALTER TABLE "coupons"."coupons" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "coupons"."coupons" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "coupons"."coupons";
    CREATE POLICY "pitako_tenant_isolation" ON "coupons"."coupons"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    ALTER TABLE "coupons"."coupon_eligibility_rules" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "coupons"."coupon_eligibility_rules" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "coupons"."coupon_eligibility_rules";
    CREATE POLICY "pitako_tenant_isolation" ON "coupons"."coupon_eligibility_rules"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    ALTER TABLE "coupons"."coupon_redemptions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "coupons"."coupon_redemptions" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "coupons"."coupon_redemptions";
    CREATE POLICY "pitako_tenant_isolation" ON "coupons"."coupon_redemptions"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM coupons.__ef_migrations_history WHERE "MigrationId" = '20261125110025_InitialCouponsSchema') THEN
    INSERT INTO coupons.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125110025_InitialCouponsSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

