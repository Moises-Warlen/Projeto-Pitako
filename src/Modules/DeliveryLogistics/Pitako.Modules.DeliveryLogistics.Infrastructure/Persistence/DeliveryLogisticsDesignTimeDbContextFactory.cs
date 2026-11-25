using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;

public sealed class DeliveryLogisticsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DeliveryLogisticsDbContext>
{
    public DeliveryLogisticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<DeliveryLogisticsDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryLogisticsDbContext.SchemaName));

        return new DeliveryLogisticsDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
