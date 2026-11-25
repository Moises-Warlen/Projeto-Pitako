using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.IntegrationTests;

/// <summary>
/// Testes de validação real de Row-Level Security (RLS) contra o servidor PostgreSQL.
/// Permanece pendente quando PITAKO_TEST_POSTGRES_CONNECTION não está configurada no ambiente.
/// </summary>
public sealed class PostgresRlsLiveTests
{
    [Fact(Skip = "Pendente de validação em ambiente PostgreSQL ativo: definir PITAKO_TEST_POSTGRES_CONNECTION com credenciais válidas.")]
    public async Task PostgresRls_ShouldIsolateRowsAtDatabaseEngineLevel()
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("PITAKO_TEST_POSTGRES_CONNECTION não configurada.");

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var contextA = new TenantContext();
        contextA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);

        var optionsA = new DbContextOptionsBuilder<TenantsDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new PostgresTenantRlsInterceptor(contextA))
            .Options;

        await using var dbA = new TenantsDbContext(optionsA, contextA, new SystemClock());
        await dbA.Database.MigrateAsync();

        dbA.StoreUnits.Add(StoreUnit.Create(tenantA, "Loja PG A", "PGA").Value);
        await dbA.SaveChangesAsync();

        await using var rawConn = new NpgsqlConnection(connectionString);
        await rawConn.OpenAsync();
        await using var cmd = rawConn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tid, false); SELECT count(*) FROM tenants.store_units;";
        cmd.Parameters.AddWithValue("tid", tenantB.ToString());
        var countForB = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        Assert.Equal(0L, countForB);
    }
}
