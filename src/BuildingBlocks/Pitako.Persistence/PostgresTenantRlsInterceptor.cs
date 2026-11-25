using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pitako.MultiTenancy;

namespace Pitako.Persistence;

/// <summary>
/// Interceptador de conexão PostgreSQL que propaga o tenant ativo para a variável de sessão
/// <c>app.current_tenant_id</c>, ativando as políticas de Row-Level Security (RLS) no banco.
/// Sempre limpa a variável no fechamento da conexão para evitar vazamento entre conexões do pool.
/// </summary>
public sealed class PostgresTenantRlsInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public PostgresTenantRlsInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
            ApplyTenantSessionSetting(npgsqlConnection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
            await ApplyTenantSessionSettingAsync(npgsqlConnection, cancellationToken);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is NpgsqlConnection npgsqlConnection && npgsqlConnection.State == System.Data.ConnectionState.Open)
            ResetTenantSessionSetting(npgsqlConnection);

        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is NpgsqlConnection npgsqlConnection && npgsqlConnection.State == System.Data.ConnectionState.Open)
            await ResetTenantSessionSettingAsync(npgsqlConnection);

        return await base.ConnectionClosingAsync(connection, eventData, result);
    }

    private void ApplyTenantSessionSetting(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant_id, false);";
        cmd.Parameters.AddWithValue("tenant_id", ResolveTenantSettingValue());
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyTenantSessionSettingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant_id, false);";
        cmd.Parameters.AddWithValue("tenant_id", ResolveTenantSettingValue());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ResetTenantSessionSetting(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false);";
        cmd.ExecuteNonQuery();
    }

    private static async Task ResetTenantSessionSettingAsync(NpgsqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false);";
        await cmd.ExecuteNonQueryAsync();
    }

    private string ResolveTenantSettingValue() =>
        _tenantContext.IsResolved ? _tenantContext.TenantId!.Value.ToString() : string.Empty;
}
