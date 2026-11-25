namespace Pitako.Persistence;

/// <summary>
/// Gerador de DDL SQL para ativação de Row-Level Security (RLS) em tabelas multi-tenant do PostgreSQL.
/// </summary>
public static class PostgresRlsSqlGenerator
{
    /// <summary>
    /// Gera instruções idempotentes para habilitar e forçar RLS com isolamento fail-closed por <c>tenant_id</c>.
    /// </summary>
    public static string BuildEnableTenantRlsSql(string schema, string tableName, string tenantColumn = "tenant_id")
    {
        ValidateIdentifier(schema, nameof(schema));
        ValidateIdentifier(tableName, nameof(tableName));
        ValidateIdentifier(tenantColumn, nameof(tenantColumn));

        return $"""
            ALTER TABLE "{schema}"."{tableName}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{tableName}" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "{schema}"."{tableName}";
            CREATE POLICY "pitako_tenant_isolation" ON "{schema}"."{tableName}"
                USING ("{tenantColumn}" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK ("{tenantColumn}" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """;
    }

    /// <summary>
    /// Gera instruções para remover a política RLS no rollback de uma migration.
    /// </summary>
    public static string BuildDisableTenantRlsSql(string schema, string tableName)
    {
        ValidateIdentifier(schema, nameof(schema));
        ValidateIdentifier(tableName, nameof(tableName));

        return $"""
            DROP POLICY IF EXISTS "pitako_tenant_isolation" ON "{schema}"."{tableName}";
            ALTER TABLE "{schema}"."{tableName}" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{tableName}" DISABLE ROW LEVEL SECURITY;
            """;
    }

    private static void ValidateIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            throw new ArgumentException($"Identificador SQL inválido: '{identifier}'.", paramName);
    }
}
