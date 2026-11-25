using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.MasterPanel.Infrastructure.Persistence;

public sealed class MasterPanelDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MasterPanelDbContext>
{
    public MasterPanelDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<MasterPanelDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", MasterPanelDbContext.SchemaName));

        return new MasterPanelDbContext(optionsBuilder.Options, new SystemClock());
    }
}
