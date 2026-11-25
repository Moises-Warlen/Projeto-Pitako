DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'security') THEN
        CREATE SCHEMA security;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS security.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'security') THEN
            CREATE SCHEMA security;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE TABLE security.security_audit_logs (
        id uuid NOT NULL,
        event_type integer NOT NULL,
        scope integer,
        tenant_id uuid,
        actor_user_id uuid,
        target_user_id uuid,
        email character varying(256),
        ip_address character varying(64),
        succeeded boolean NOT NULL,
        action_description character varying(500) NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT "PK_security_audit_logs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE TABLE security.users (
        id uuid NOT NULL,
        scope integer NOT NULL,
        tenant_id uuid,
        full_name character varying(180) NOT NULL,
        email character varying(256) NOT NULL,
        normalized_email character varying(256) NOT NULL,
        password_hash character varying(512) NOT NULL,
        saas_role integer,
        establishment_role integer,
        is_active boolean NOT NULL,
        failed_login_attempts integer NOT NULL,
        lockout_end_utc timestamp with time zone,
        security_stamp character varying(64) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        CONSTRAINT "PK_users" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE TABLE security.password_reset_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        used_at_utc timestamp with time zone,
        invalidated_at_utc timestamp with time zone,
        requested_from_ip character varying(64),
        CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY (id),
        CONSTRAINT "FK_password_reset_tokens_users_user_id" FOREIGN KEY (user_id) REFERENCES security.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE TABLE security.user_module_permissions (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        module_code character varying(64) NOT NULL,
        allowed_actions integer NOT NULL,
        CONSTRAINT "PK_user_module_permissions" PRIMARY KEY (id),
        CONSTRAINT "FK_user_module_permissions_users_user_id" FOREIGN KEY (user_id) REFERENCES security.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE TABLE security.user_sessions (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        tenant_id uuid,
        refresh_token_hash character varying(128) NOT NULL,
        issued_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        revoked_at_utc timestamp with time zone,
        revocation_reason character varying(200),
        ip_address character varying(64),
        user_agent character varying(512),
        device_id character varying(128),
        CONSTRAINT "PK_user_sessions" PRIMARY KEY (id),
        CONSTRAINT "FK_user_sessions_users_user_id" FOREIGN KEY (user_id) REFERENCES security.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE UNIQUE INDEX "IX_password_reset_tokens_token_hash" ON security.password_reset_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE INDEX "IX_password_reset_tokens_user_id" ON security.password_reset_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE INDEX "IX_security_audit_logs_occurred_at_utc" ON security.security_audit_logs (occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE INDEX "IX_security_audit_logs_tenant_id_occurred_at_utc" ON security.security_audit_logs (tenant_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE UNIQUE INDEX "IX_user_module_permissions_user_id_module_code" ON security.user_module_permissions (user_id, module_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE UNIQUE INDEX "IX_user_sessions_refresh_token_hash" ON security.user_sessions (refresh_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE INDEX "IX_user_sessions_user_id_revoked_at_utc_expires_at_utc" ON security.user_sessions (user_id, revoked_at_utc, expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    CREATE UNIQUE INDEX "IX_users_scope_tenant_id_normalized_email" ON security.users (scope, tenant_id, normalized_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM security.__ef_migrations_history WHERE "MigrationId" = '20261125010416_InitialSecuritySchema') THEN
    INSERT INTO security.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125010416_InitialSecuritySchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

