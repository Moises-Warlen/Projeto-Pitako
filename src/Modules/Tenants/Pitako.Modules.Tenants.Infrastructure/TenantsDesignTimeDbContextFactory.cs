using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Tenants.Infrastructure;

/// <summary>
/// Factory de design-time para geração de migrations do módulo Tenants via <c>dotnet ef</c>.
/// Não contém segredos; utiliza string de conexão dummy apenas quando nenhuma variável de ambiente é informada.
/// </summary>
public sealed class TenantsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<TenantsDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", TenantsDbContext.SchemaName));

        return new TenantsDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
