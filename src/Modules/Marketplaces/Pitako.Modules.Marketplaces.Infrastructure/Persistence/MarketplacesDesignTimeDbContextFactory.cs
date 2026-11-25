using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Marketplaces.Infrastructure.Persistence;

public sealed class MarketplacesDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MarketplacesDbContext>
{
    public MarketplacesDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PITAKO_POSTGRES_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=pitako_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MarketplacesDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", MarketplacesDbContext.SchemaName))
            .Options;

        return new MarketplacesDbContext(options, new TenantContext(), new SystemClock());
    }
}
