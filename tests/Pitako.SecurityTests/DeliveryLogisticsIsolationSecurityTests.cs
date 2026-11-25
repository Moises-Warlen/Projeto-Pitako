using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class DeliveryLogisticsIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private DeliveryLogisticsDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadOrModify_DeliveryPoliciesOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var courierA = User.CreateEstablishmentUser(tenantA, "Entregador A", "ent@a.com", "hash", EstablishmentRole.Courier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid policyBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(dbB), ctxB);
            var pol = (await svcB.CreatePolicyAsync(adminB, -23.5, -46.6, 5m, FreightCalculationMode.FixedFlat, 10m)).Value;
            policyBId = pol.Id;
            await svcB.AddDistanceTierAsync(adminB, policyBId, 0m, 5m, 10m, 30);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(dbA), ctxA);

            // Global query filter oculta políticas, faixas e zonas da Loja B
            Assert.Empty(await dbA.DeliveryPolicies.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.DistanceTiers.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.ZoneRules.AsNoTracking().ToListAsync());

            var pauseB = await svcA.PauseDeliveryAsync(adminA, policyBId, "Invasão");
            Assert.True(pauseB.IsFailure);
            Assert.Equal("Delivery.PolicyNotFound", pauseB.Error.Code);

            // Menor privilégio: entregador não pode criar nem alterar política comercial de frete
            var courierCreate = await svcA.CreatePolicyAsync(courierA, -23.5, -46.6, 10m, FreightCalculationMode.FixedFlat, 1m);
            Assert.True(courierCreate.IsFailure);
            Assert.Equal("Delivery.Forbidden", courierCreate.Error.Code);
        }
    }
}
