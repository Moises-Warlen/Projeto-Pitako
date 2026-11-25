using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.PdvCashier.Infrastructure.Persistence;

public sealed class PdvCashierDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PdvCashierDbContext>
{
    public PdvCashierDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<PdvCashierDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", PdvCashierDbContext.SchemaName));

        return new PdvCashierDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
