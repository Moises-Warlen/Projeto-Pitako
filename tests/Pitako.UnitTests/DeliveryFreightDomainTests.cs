using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class DeliveryFreightDomainTests
{
    [Fact]
    public void GeoDistanceCalculator_ShouldComputeHaversineKilometersAccurately()
    {
        var paulista = GeoCoordinate.Create(-23.5613, -46.6565).Value;
        var ibirapuera = GeoCoordinate.Create(-23.5874, -46.6576).Value;

        var km = GeoDistanceCalculator.CalculateKilometers(paulista, ibirapuera);
        Assert.InRange(km, 2.70m, 3.10m);
    }

    [Fact]
    public void DeliveryPolicy_FixedPlusPerKm_ShouldEnforceRadiusMinimumBlockedZoneAndFreeThreshold()
    {
        var tenantId = Guid.NewGuid();
        var policy = DeliveryPolicy.Create(
            tenantId,
            originLatitude: -23.5613,
            originLongitude: -46.6565,
            maxRadiusKm: 8m,
            calculationMode: FreightCalculationMode.FixedPlusPerKm,
            baseFee: Money.Create(6.00m).Value,
            includedKm: 2m,
            feePerKm: Money.Create(2.50m).Value,
            baseEstimatedMinutes: 25,
            minimumOrderAmount: Money.Create(25.00m).Value,
            freeShippingThreshold: Money.Create(90.00m).Value).Value;

        policy.AddZoneRule("Área de Risco", "09999", Money.Zero(), 60, isBlocked: true);

        // Pedido abaixo do mínimo -> falha
        var minFail = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(20m).Value, PrecomputedDistanceKm: 2m));
        Assert.True(minFail.IsFailure);
        Assert.Equal("Delivery.MinimumOrderNotReached", minFail.Error.Code);

        // Região bloqueada -> falha
        var blockedFail = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(50m).Value, ZipCode: "09999-100", PrecomputedDistanceKm: 1m));
        Assert.True(blockedFail.IsFailure);
        Assert.Equal("Delivery.ZoneBlocked", blockedFail.Error.Code);

        // Acima do raio máximo -> falha
        var radiusFail = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(50m).Value, PrecomputedDistanceKm: 9.5m));
        Assert.True(radiusFail.IsFailure);
        Assert.Equal("Delivery.OutOfCoverageRadius", radiusFail.Error.Code);

        // 4 km: 2 km incluídos + 2 km × R$2,50 = 6,00 + 5,00 = R$ 11,00
        var quote4km = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(50m).Value, PrecomputedDistanceKm: 4m));
        Assert.True(quote4km.IsSuccess);
        Assert.False(quote4km.Value.IsFreeShippingApplied);
        Assert.Equal(11.00m, quote4km.Value.FinalDeliveryFee.Amount);

        // Pedido >= R$ 90,00 -> isenção automática mantendo BaseDeliveryFee original (R$11,00) e Final = R$0,00
        var freeQuote = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(95m).Value, PrecomputedDistanceKm: 4m));
        Assert.True(freeQuote.IsSuccess);
        Assert.True(freeQuote.Value.IsFreeShippingApplied);
        Assert.Equal(11.00m, freeQuote.Value.BaseDeliveryFee.Amount);
        Assert.Equal(0.00m, freeQuote.Value.FinalDeliveryFee.Amount);

        // Pausa temporária bloqueia delivery
        policy.PauseDelivery("Chuva intensa na região");
        var paused = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(50m).Value, PrecomputedDistanceKm: 2m));
        Assert.True(paused.IsFailure);
        Assert.Equal("Delivery.Paused", paused.Error.Code);
    }

    [Fact]
    public void DeliveryPolicy_DistanceTiered_ShouldRejectOverlappingTiersAndMatchCorrectRange()
    {
        var policy = DeliveryPolicy.Create(
            Guid.NewGuid(),
            -23.55, -46.63,
            10m,
            FreightCalculationMode.DistanceTiered,
            Money.Zero(), 0m, Money.Zero(), 30, Money.Zero()).Value;

        Assert.True(policy.AddDistanceTier(0m, 3m, Money.Create(5m).Value, 25).IsSuccess);
        Assert.True(policy.AddDistanceTier(3m, 6m, Money.Create(10m).Value, 40).IsSuccess);

        var overlap = policy.AddDistanceTier(2m, 5m, Money.Create(8m).Value, 30);
        Assert.True(overlap.IsFailure);
        Assert.Equal("Delivery.OverlappingTier", overlap.Error.Code);

        var q = policy.QuoteFreight(new DeliveryFreightQuoteRequest(Money.Create(40m).Value, PrecomputedDistanceKm: 4.5m));
        Assert.True(q.IsSuccess);
        Assert.Equal(10.00m, q.Value.FinalDeliveryFee.Amount);
        Assert.Equal(40, q.Value.EstimatedMinutes);
    }
}
