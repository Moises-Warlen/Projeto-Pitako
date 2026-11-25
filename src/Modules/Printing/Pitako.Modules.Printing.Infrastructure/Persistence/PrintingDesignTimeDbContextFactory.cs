using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Printing.Infrastructure.Persistence;

public sealed class PrintingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PrintingDbContext>
{
    public PrintingDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PITAKO_POSTGRES_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=pitako_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PrintingDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PrintingDbContext.SchemaName))
            .Options;

        return new PrintingDbContext(options, new TenantContext(), new SystemClock());
    }
}
