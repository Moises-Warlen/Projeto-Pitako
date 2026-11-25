using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.OfflineSync.Infrastructure.Persistence;

public sealed class OfflineSyncDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OfflineSyncDbContext>
{
    public OfflineSyncDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PITAKO_POSTGRES_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=pitako_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OfflineSyncDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", OfflineSyncDbContext.SchemaName))
            .Options;

        return new OfflineSyncDbContext(options, new TenantContext(), new SystemClock());
    }
}
