using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Reports.Infrastructure.Persistence;

public sealed class ReportsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReportsDbContext>
{
    public ReportsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PITAKO_POSTGRES_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=pitako_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ReportsDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ReportsDbContext.SchemaName))
            .Options;

        return new ReportsDbContext(options, new TenantContext(), new SystemClock());
    }
}
