using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class DeliveryCouriersAndRoutingTests
{
    [Fact]
    public void DispatchRun_ShouldOptimizeStopsByNearestNeighbor_ComputeCommissions_AndRestoreCourierAvailability()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero);
        var storeOrigin = GeoCoordinate.Create(-23.5613, -46.6565).Value;

        // Entregador com R$ 4,00 fixo + 20% do frete por entrega
        var courier = DeliveryCourier.Create(
            tenantId, "Carlos Motoboy", "11988887777", CourierVehicleType.Motorcycle,
            CourierCommissionMode.FixedPlusPercentage, Money.Create(4.00m).Value, 20m, null, "ABC1D23").Value;

        var orderFar = new DispatchStopInput(
            Guid.NewGuid(), "#0010", "Cliente Longe", "Av. Santo Amaro, 3000", -23.6200, -46.6900, Money.Create(15.00m).Value);
        var orderNear = new DispatchStopInput(
            Guid.NewGuid(), "#0011", "Cliente Perto", "Al. Santos, 200", -23.5680, -46.6540, Money.Create(10.00m).Value);

        // Passa o pedido distante primeiro; otimizador deve colocar o mais próximo em sequência 1
        var run = DeliveryDispatchRun.CreateOptimized(
            tenantId, courier, "ROTA-0001", storeOrigin, [orderFar, orderNear], utc0, "Saída agrupada").Value;

        Assert.Equal(CourierAvailabilityStatus.OnRoute, courier.Availability);
        Assert.Equal(2, run.Stops.Count);
        var stop1 = run.Stops.Single(s => s.StopSequence == 1);
        var stop2 = run.Stops.Single(s => s.StopSequence == 2);
        Assert.Equal("#0011", stop1.OrderNumberSnapshot);
        Assert.Equal("#0010", stop2.OrderNumberSnapshot);

        // Comissão parada 1: 4 + 20% de 10 = 6,00; parada 2: 4 + 20% de 15 = 7,00 -> Total 13,00
        Assert.Equal(6.00m, stop1.CourierCommission.Amount);
        Assert.Equal(7.00m, stop2.CourierCommission.Amount);
        Assert.Equal(13.00m, run.TotalCommission.Amount);

        // Entregador em rota não aceita nova corrida simultânea
        var doubleDispatch = DeliveryDispatchRun.CreateOptimized(tenantId, courier, "ROTA-0002", storeOrigin, [orderNear], utc0);
        Assert.True(doubleDispatch.IsFailure);
        Assert.Equal("Courier.Unavailable", doubleDispatch.Error.Code);

        // Ocorrência sem motivo falha
        var failNoReason = run.ResolveStop(stop1.Id, delivered: false, " ", courier, utc0.AddMinutes(10));
        Assert.True(failNoReason.IsFailure);
        Assert.Equal("Dispatch.FailureReasonRequired", failNoReason.Error.Code);

        Assert.True(run.StartRoute(utc0.AddMinutes(2)).IsSuccess);
        Assert.True(run.ResolveStop(stop1.Id, delivered: true, "Entregue na portaria", courier, utc0.AddMinutes(12)).IsSuccess);
        Assert.Equal(DispatchRunStatus.InTransit, run.Status);

        Assert.True(run.ResolveStop(stop2.Id, delivered: true, "Entregue em mãos", courier, utc0.AddMinutes(25)).IsSuccess);
        Assert.Equal(DispatchRunStatus.Completed, run.Status);
        Assert.Equal(CourierAvailabilityStatus.Available, courier.Availability);
    }
}
