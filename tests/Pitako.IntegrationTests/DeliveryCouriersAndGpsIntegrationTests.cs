using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class RecordingCourierGpsNotifier : ICourierGpsRealtimeNotifier
{
    public List<CourierGpsRealtimeEvent> Events { get; } = [];

    public Task NotifyLocationAsync(CourierGpsRealtimeEvent gpsEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(gpsEvent);
        return Task.CompletedTask;
    }
}

public sealed class DeliveryCouriersAndGpsIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero));

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
    public async Task Couriers_RouteGrouping_GpsTracking_AndRunsReport_ShouldWorkEndToEnd()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var courierUser = User.CreateEstablishmentUser(tenantId, "Marcos Entregador", "marcos@loja.com", "hash", EstablishmentRole.Courier).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);
        var notifier = new RecordingCourierGpsNotifier();

        Guid courierId;
        Guid runId;
        Guid stop1Id;
        Guid stop2Id;

        await using (var db = CreateDb(ctx))
        {
            var svc = new CourierDispatchAndGpsService(
                new EfDeliveryCourierRepository(db),
                new EfDeliveryDispatchRunRepository(db),
                new EfCourierGpsPingRepository(db),
                notifier,
                ctx,
                _clock);

            var courier = (await svc.RegisterCourierAsync(
                manager,
                "Marcos Entregador",
                "11977776666",
                CourierVehicleType.Motorcycle,
                CourierCommissionMode.FixedPerDelivery,
                fixedCommissionAmount: 7.00m,
                linkedUserId: courierUser.Id,
                licensePlate: "XYZ9K88")).Value;
            courierId = courier.Id;

            var run = (await svc.CreateAndOptimizeDispatchRunAsync(
                manager,
                courierId,
                -23.5613, -46.6565,
                [
                    new DispatchStopInput(Guid.NewGuid(), "#0020", "Ana", "Rua B, 200", -23.600, -46.670, Money.Create(14m).Value),
                    new DispatchStopInput(Guid.NewGuid(), "#0021", "Bruno", "Rua A, 50", -23.565, -46.657, Money.Create(8m).Value)
                ])).Value;

            runId = run.Id;
            stop1Id = run.Stops.Single(s => s.StopSequence == 1).Id;
            stop2Id = run.Stops.Single(s => s.StopSequence == 2).Id;

            // Entregador inicia sua corrida e envia telemetria GPS em tempo real
            Assert.True((await svc.StartRunAsync(courierUser, runId)).IsSuccess);
            await svc.RecordGpsPingAsync(courierUser, courierId, -23.563, -46.656, runId, 38.5m);
            _clock.UtcNow = _clock.UtcNow.AddMinutes(4);
            await svc.RecordGpsPingAsync(courierUser, courierId, -23.580, -46.662, runId, 42.0m);

            Assert.Equal(2, notifier.Events.Count);
        }

        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new CourierDispatchAndGpsService(
                new EfDeliveryCourierRepository(db2),
                new EfDeliveryDispatchRunRepository(db2),
                new EfCourierGpsPingRepository(db2),
                notifier,
                ctx,
                _clock);

            // Entregador resolve parada 1 (entregue) e parada 2 (cliente ausente)
            await svc2.ResolveStopAsync(courierUser, runId, stop1Id, delivered: true, "Entregue OK");
            _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
            var completed = await svc2.ResolveStopAsync(courierUser, runId, stop2Id, delivered: false, "Cliente não atendeu interfone");

            Assert.True(completed.IsSuccess);
            Assert.Equal(DispatchRunStatus.Completed, completed.Value.Status);

            var courierReloaded = await db2.Couriers.AsNoTracking().FirstAsync(c => c.Id == courierId);
            Assert.Equal(CourierAvailabilityStatus.Available, courierReloaded.Availability);
            Assert.Equal(-23.580, courierReloaded.LastKnownLatitude);

            // Relatório de corridas contabiliza apenas entregas concluídas na comissão/frete e preserva ocorrências
            var report = (await svc2.GetCourierRunsReportAsync(manager, courierId, _clock.UtcNow.AddHours(-1), _clock.UtcNow.AddHours(1))).Value;
            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(1, report.DeliveredStopsCount);
            Assert.Equal(1, report.FailedStopsCount);
            Assert.Equal(8.00m, report.TotalFreightFees.Amount);
            Assert.Equal(7.00m, report.TotalCourierCommission.Amount);
        }
    }
}
