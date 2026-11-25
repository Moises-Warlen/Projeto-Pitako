using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;

public sealed class AcaiBuilderDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AcaiBuilderDbContext>
{
    public AcaiBuilderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<AcaiBuilderDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", AcaiBuilderDbContext.SchemaName));

        return new AcaiBuilderDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
