DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'delivery') THEN
        CREATE SCHEMA delivery;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS delivery.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'delivery') THEN
            CREATE SCHEMA delivery;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE TABLE delivery.delivery_policies (
        id uuid NOT NULL,
        store_unit_id uuid,
        origin_latitude double precision NOT NULL,
        origin_longitude double precision NOT NULL,
        max_radius_km numeric(10,2) NOT NULL,
        calculation_mode integer NOT NULL,
        base_fee_amount numeric(18,2) NOT NULL,
        base_fee_currency character varying(3) NOT NULL,
        included_km numeric(10,2) NOT NULL,
        fee_per_km_amount numeric(18,2) NOT NULL,
        fee_per_km_currency character varying(3) NOT NULL,
        base_estimated_minutes integer NOT NULL,
        minimum_order_amount numeric(18,2) NOT NULL,
        minimum_order_currency character varying(3) NOT NULL,
        free_shipping_threshold_amount numeric(18,2),
        free_shipping_threshold_currency character varying(3),
        is_delivery_enabled boolean NOT NULL,
        allow_pickup boolean NOT NULL,
        is_temporarily_paused boolean NOT NULL,
        pause_reason character varying(240),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_delivery_policies" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE TABLE delivery.delivery_distance_tiers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_policy_id uuid NOT NULL,
        min_km numeric(10,2) NOT NULL,
        max_km numeric(10,2) NOT NULL,
        fee_amount numeric(18,2) NOT NULL,
        fee_currency character varying(3) NOT NULL,
        estimated_minutes integer NOT NULL,
        CONSTRAINT "PK_delivery_distance_tiers" PRIMARY KEY (id),
        CONSTRAINT "FK_delivery_distance_tiers_delivery_policies_delivery_policy_id" FOREIGN KEY (delivery_policy_id) REFERENCES delivery.delivery_policies (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE TABLE delivery.delivery_zone_rules (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_policy_id uuid NOT NULL,
        zone_name character varying(140) NOT NULL,
        zip_code_prefix character varying(16),
        fee_amount numeric(18,2) NOT NULL,
        fee_currency character varying(3) NOT NULL,
        estimated_minutes integer NOT NULL,
        is_blocked boolean NOT NULL,
        CONSTRAINT "PK_delivery_zone_rules" PRIMARY KEY (id),
        CONSTRAINT "FK_delivery_zone_rules_delivery_policies_delivery_policy_id" FOREIGN KEY (delivery_policy_id) REFERENCES delivery.delivery_policies (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE INDEX "IX_delivery_distance_tiers_delivery_policy_id" ON delivery.delivery_distance_tiers (delivery_policy_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE INDEX "IX_delivery_distance_tiers_tenant_id_delivery_policy_id_min_km" ON delivery.delivery_distance_tiers (tenant_id, delivery_policy_id, min_km);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE INDEX "IX_delivery_policies_tenant_id_store_unit_id" ON delivery.delivery_policies (tenant_id, store_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE INDEX "IX_delivery_zone_rules_delivery_policy_id" ON delivery.delivery_zone_rules (delivery_policy_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    CREATE INDEX "IX_delivery_zone_rules_tenant_id_delivery_policy_id_zone_name" ON delivery.delivery_zone_rules (tenant_id, delivery_policy_id, zone_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    ALTER TABLE "delivery"."delivery_policies" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_policies" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_policies";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_policies"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    ALTER TABLE "delivery"."delivery_distance_tiers" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_distance_tiers" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_distance_tiers";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_distance_tiers"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    ALTER TABLE "delivery"."delivery_zone_rules" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_zone_rules" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_zone_rules";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_zone_rules"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125111136_InitialDeliveryPolicySchema') THEN
    INSERT INTO delivery.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125111136_InitialDeliveryPolicySchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE TABLE delivery.courier_gps_pings (
        id uuid NOT NULL,
        courier_id uuid NOT NULL,
        active_dispatch_run_id uuid,
        latitude double precision NOT NULL,
        longitude double precision NOT NULL,
        speed_kmh numeric(6,2),
        recorded_at_utc timestamp with time zone NOT NULL,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_courier_gps_pings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE TABLE delivery.delivery_couriers (
        id uuid NOT NULL,
        linked_user_id uuid,
        full_name character varying(160) NOT NULL,
        phone character varying(40) NOT NULL,
        vehicle_type integer NOT NULL,
        license_plate character varying(20),
        commission_mode integer NOT NULL,
        fixed_commission_amount numeric(18,2) NOT NULL,
        fixed_commission_currency character varying(3) NOT NULL,
        freight_commission_percentage numeric(5,2) NOT NULL,
        availability integer NOT NULL,
        last_known_latitude double precision,
        last_known_longitude double precision,
        last_location_at_utc timestamp with time zone,
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_delivery_couriers" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE TABLE delivery.delivery_dispatch_runs (
        id uuid NOT NULL,
        courier_id uuid NOT NULL,
        run_code character varying(32) NOT NULL,
        status integer NOT NULL,
        total_estimated_distance_km numeric(10,2) NOT NULL,
        total_commission_amount numeric(18,2) NOT NULL,
        total_commission_currency character varying(3) NOT NULL,
        assigned_at_utc timestamp with time zone NOT NULL,
        dispatched_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        notes character varying(400),
        tenant_id uuid NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_delivery_dispatch_runs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE TABLE delivery.delivery_run_stops (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        delivery_dispatch_run_id uuid NOT NULL,
        order_id uuid NOT NULL,
        order_number_snapshot character varying(32) NOT NULL,
        customer_name_snapshot character varying(160) NOT NULL,
        destination_address character varying(400) NOT NULL,
        destination_latitude double precision NOT NULL,
        destination_longitude double precision NOT NULL,
        stop_sequence integer NOT NULL,
        leg_distance_km numeric(10,2) NOT NULL,
        delivery_fee_amount numeric(18,2) NOT NULL,
        delivery_fee_currency character varying(3) NOT NULL,
        courier_commission_amount numeric(18,2) NOT NULL,
        courier_commission_currency character varying(3) NOT NULL,
        status integer NOT NULL,
        completed_at_utc timestamp with time zone,
        resolution_notes character varying(300),
        CONSTRAINT "PK_delivery_run_stops" PRIMARY KEY (id),
        CONSTRAINT "FK_delivery_run_stops_delivery_dispatch_runs_delivery_dispatch~" FOREIGN KEY (delivery_dispatch_run_id) REFERENCES delivery.delivery_dispatch_runs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE INDEX "IX_courier_gps_pings_tenant_id_courier_id_recorded_at_utc" ON delivery.courier_gps_pings (tenant_id, courier_id, recorded_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE INDEX "IX_delivery_couriers_tenant_id_availability" ON delivery.delivery_couriers (tenant_id, availability);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE INDEX "IX_delivery_dispatch_runs_tenant_id_courier_id_assigned_at_utc" ON delivery.delivery_dispatch_runs (tenant_id, courier_id, assigned_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE INDEX "IX_delivery_run_stops_delivery_dispatch_run_id" ON delivery.delivery_run_stops (delivery_dispatch_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    CREATE INDEX "IX_delivery_run_stops_tenant_id_delivery_dispatch_run_id_stop_~" ON delivery.delivery_run_stops (tenant_id, delivery_dispatch_run_id, stop_sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    ALTER TABLE "delivery"."delivery_couriers" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_couriers" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_couriers";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_couriers"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    ALTER TABLE "delivery"."delivery_dispatch_runs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_dispatch_runs" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_dispatch_runs";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_dispatch_runs"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    ALTER TABLE "delivery"."delivery_run_stops" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."delivery_run_stops" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."delivery_run_stops";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."delivery_run_stops"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    ALTER TABLE "delivery"."courier_gps_pings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "delivery"."courier_gps_pings" FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "delivery"."courier_gps_pings";
    CREATE POLICY "pitako_tenant_isolation" ON "delivery"."courier_gps_pings"
        USING ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
        WITH CHECK ("tenant_id" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM delivery.__ef_migrations_history WHERE "MigrationId" = '20261125113515_AddCouriersRoutesAndGps') THEN
    INSERT INTO delivery.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125113515_AddCouriersRoutesAndGps', '8.0.11');
    END IF;
END $EF$;
COMMIT;

