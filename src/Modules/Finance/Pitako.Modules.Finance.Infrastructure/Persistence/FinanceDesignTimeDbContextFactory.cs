using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PITAKO_POSTGRES_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=pitako_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", FinanceDbContext.SchemaName))
            .Options;

        return new FinanceDbContext(options, new TenantContext(), new SystemClock());
    }
}
