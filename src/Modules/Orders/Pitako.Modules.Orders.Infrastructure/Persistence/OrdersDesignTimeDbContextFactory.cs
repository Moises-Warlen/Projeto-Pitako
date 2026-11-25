using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Orders.Infrastructure.Persistence;

public sealed class OrdersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PITAKO_MIGRATION_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pitako_design;Username=pitako_design;Password=design_only_placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", OrdersDbContext.SchemaName));

        return new OrdersDbContext(optionsBuilder.Options, new TenantContext(), new SystemClock());
    }
}
