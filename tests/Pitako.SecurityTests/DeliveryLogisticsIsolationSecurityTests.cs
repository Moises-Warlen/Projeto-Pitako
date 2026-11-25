using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecCourierGpsNotifier : ICourierGpsRealtimeNotifier
{
    public Task NotifyLocationAsync(CourierGpsRealtimeEvent gpsEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

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

    [Fact]
    public async Task TenantA_And_OtherCouriers_ShouldNeverOperateOrView_CouriersRunsAndGpsOfOthers()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var courierA1 = User.CreateEstablishmentUser(tenantA, "Entregador A1", "e1@a.com", "hash", EstablishmentRole.Courier).Value;
        var courierA2 = User.CreateEstablishmentUser(tenantA, "Entregador A2", "e2@a.com", "hash", EstablishmentRole.Courier).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid courierBId;
        Guid runBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new CourierDispatchAndGpsService(new EfDeliveryCourierRepository(dbB), new EfDeliveryDispatchRunRepository(dbB), new EfCourierGpsPingRepository(dbB), new SecCourierGpsNotifier(), ctxB, _clock);
            var cb = (await svcB.RegisterCourierAsync(adminB, "Entregador B", "119", CourierVehicleType.Motorcycle, CourierCommissionMode.FixedPerDelivery, 6m)).Value;
            courierBId = cb.Id;
            var rb = (await svcB.CreateAndOptimizeDispatchRunAsync(adminB, courierBId, -23.5, -46.6, [new DispatchStopInput(Guid.NewGuid(), "#B1", "Cli B", "Rua B", -23.51, -46.61, Money.Create(10m).Value)])).Value;
            runBId = rb.Id;
            await svcB.RecordGpsPingAsync(adminB, courierBId, -23.505, -46.605, runBId);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new CourierDispatchAndGpsService(new EfDeliveryCourierRepository(dbA), new EfDeliveryDispatchRunRepository(dbA), new EfCourierGpsPingRepository(dbA), new SecCourierGpsNotifier(), ctxA, _clock);

        // Global query filter oculta entregadores, corridas, paradas e pings de B
        Assert.Empty(await dbA.Couriers.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.DispatchRuns.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.RunStops.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.GpsPings.AsNoTracking().ToListAsync());

        // Admin A não acessa relatório nem corrida da Loja B
        var reportB = await svcA.GetCourierRunsReportAsync(adminA, courierBId, _clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1));
        Assert.True(reportB.IsFailure);
        Assert.Equal("Courier.NotFound", reportB.Error.Code);

        // Na mesma loja A: Entregador A2 NÃO pode atualizar GPS nem iniciar corrida pertencente ao Entregador A1
        var cA1 = (await svcA.RegisterCourierAsync(adminA, "Entregador A1", "1191", CourierVehicleType.Bicycle, CourierCommissionMode.FixedPerDelivery, 5m, linkedUserId: courierA1.Id)).Value;
        var runA1 = (await svcA.CreateAndOptimizeDispatchRunAsync(adminA, cA1.Id, -23.5, -46.6, [new DispatchStopInput(Guid.NewGuid(), "#A1", "Cli A", "Rua A", -23.51, -46.61, Money.Create(8m).Value)])).Value;

        var spoofGps = await svcA.RecordGpsPingAsync(courierA2, cA1.Id, -23.5, -46.6);
        Assert.True(spoofGps.IsFailure);
        Assert.Equal("Courier.CannotOperateAnotherCourier", spoofGps.Error.Code);

        var hijackStart = await svcA.StartRunAsync(courierA2, runA1.Id);
        Assert.True(hijackStart.IsFailure);
        Assert.Equal("Courier.CannotOperateAnotherCourier", hijackStart.Error.Code);

        // Entregador não pode cadastrar outro entregador (apenas View | Edit)
        var courierCreateAttempt = await svcA.RegisterCourierAsync(courierA1, "Fake", "1192", CourierVehicleType.OnFoot, CourierCommissionMode.FixedPerDelivery, 5m);
        Assert.True(courierCreateAttempt.IsFailure);
        Assert.Equal("Delivery.Forbidden", courierCreateAttempt.Error.Code);
    }
}
