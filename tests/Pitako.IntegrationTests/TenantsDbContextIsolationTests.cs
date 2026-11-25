using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow => utcNow;
}

public sealed class TenantsDbContextIsolationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedNow = new(2026, 11, 24, 21, 50, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var setupContext = CreateContext(ctx => ctx.SetTenant(_tenantAId, TenantResolutionSource.AuthenticatedSession));
        await setupContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private TenantsDbContext CreateContext(Action<ITenantContextSetter>? configureTenant = null)
    {
        var tenantContext = new TenantContext();
        configureTenant?.Invoke(tenantContext);

        var options = new DbContextOptionsBuilder<TenantsDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new TenantsDbContext(options, tenantContext, new FixedClock(_fixedNow));
    }

    [Fact]
    public async Task QueryFilter_ShouldIsolateStoreUnitsBetweenTenants_AcrossDbContextInstances()
    {
        await using (var dbA = CreateContext(c => c.SetTenant(_tenantAId, TenantResolutionSource.AuthenticatedSession)))
        {
            var unitA = StoreUnit.Create(_tenantAId, "Açaí do Centro", "MATRIZ", isHeadquarters: true).Value;
            dbA.StoreUnits.Add(unitA);
            await dbA.SaveChangesAsync();
        }

        await using (var dbB = CreateContext(c => c.SetTenant(_tenantBId, TenantResolutionSource.AuthenticatedSession)))
        {
            var unitB = StoreUnit.Create(_tenantBId, "Pizzaria Sul", "FILIAL-01").Value;
            dbB.StoreUnits.Add(unitB);
            await dbB.SaveChangesAsync();

            var visibleToB = await dbB.StoreUnits.AsNoTracking().ToListAsync();
            Assert.Single(visibleToB);
            Assert.Equal(_tenantBId, visibleToB[0].TenantId);
            Assert.Equal("Pizzaria Sul", visibleToB[0].Name);
        }

        await using (var dbAVerify = CreateContext(c => c.SetTenant(_tenantAId, TenantResolutionSource.AuthenticatedSession)))
        {
            var visibleToA = await dbAVerify.StoreUnits.AsNoTracking().ToListAsync();
            Assert.Single(visibleToA);
            Assert.Equal(_tenantAId, visibleToA[0].TenantId);
            Assert.Equal("Açaí do Centro", visibleToA[0].Name);
        }
    }

    [Fact]
    public async Task QueryFilter_ShouldFailClosed_WhenTenantContextIsUnresolved()
    {
        await using (var dbA = CreateContext(c => c.SetTenant(_tenantAId, TenantResolutionSource.AuthenticatedSession)))
        {
            dbA.StoreUnits.Add(StoreUnit.Create(_tenantAId, "Loja Isolada", "U1").Value);
            await dbA.SaveChangesAsync();
        }

        await using (var unresolvedDb = CreateContext())
        {
            var units = await unresolvedDb.StoreUnits.AsNoTracking().ToListAsync();
            Assert.Empty(units);
        }
    }

    [Fact]
    public async Task SaveChanges_ShouldPopulateAuditTimestamps_AndEnforceTenantId()
    {
        await using var dbA = CreateContext(c => c.SetTenant(_tenantAId, TenantResolutionSource.AuthenticatedSession));
        var unit = StoreUnit.Create(_tenantAId, "Hamburgueria Norte", "HN-01").Value;

        dbA.StoreUnits.Add(unit);
        await dbA.SaveChangesAsync();

        Assert.Equal(_fixedNow, unit.CreatedAtUtc);
        Assert.Null(unit.UpdatedAtUtc);
    }

    [Fact]
    public void PostgresRlsSqlGenerator_ShouldProduceForceRowLevelSecurityAndFailClosedPolicy()
    {
        var sql = PostgresRlsSqlGenerator.BuildEnableTenantRlsSql("tenants", "store_units");

        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql);
        Assert.Contains("NULLIF(current_setting('app.current_tenant_id', true), '')::uuid", sql);
    }
}
