using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.DiningHall.Infrastructure.Persistence;

public sealed class DiningHallDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DiningHallDbContext>
{
    public DiningHallDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<DiningHallDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", DiningHallDbContext.SchemaName));

        return new DiningHallDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
