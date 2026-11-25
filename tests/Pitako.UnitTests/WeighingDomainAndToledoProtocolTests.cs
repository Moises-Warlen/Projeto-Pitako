using Pitako.Modules.Weighing.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class WeighingDomainAndToledoProtocolTests
{
    [Theory]
    [InlineData("\u000200450\u0003", 450)]
    [InlineData("\u0002001250\u0003", 1250)]
    [InlineData("\u0002S,01.250kg\u0003", 1250)]
    [InlineData("00.785", 785)]
    public void ToledoScaleProtocolParser_ShouldDecodeValidFrames(string rawFrame, decimal expectedGrams)
    {
        var parsed = ToledoScaleProtocolParser.ParseFrame(rawFrame);
        Assert.True(parsed.IsSuccess);
        Assert.True(parsed.Value.IsStable);
        Assert.Equal(expectedGrams, parsed.Value.GrossWeightGrams);
    }

    [Theory]
    [InlineData("\u0002IIIII\u0003", "Scale.UnstableWeight")]
    [InlineData("\u0002U,00.450\u0003", "Scale.UnstableWeight")]
    [InlineData("\u0002SSSSS\u0003", "Scale.Overload")]
    [InlineData("\u0002NNNNN\u0003", "Scale.Underload")]
    [InlineData("XYZ_CORRUPT", "Scale.InvalidFrame")]
    public void ToledoScaleProtocolParser_ShouldRejectUnstableOverloadUnderloadAndCorruptFrames(string rawFrame, string expectedError)
    {
        var parsed = ToledoScaleProtocolParser.ParseFrame(rawFrame);
        Assert.True(parsed.IsFailure);
        Assert.Equal(expectedError, parsed.Error.Code);
    }

    [Fact]
    public void WeightPriceCalculator_And_WeighingRecord_ShouldComputeNetWeightAuditAndRejectInvalidWeights()
    {
        var pricePerKg = Money.Create(50.00m).Value;

        // 450g bruto - 20g tara = 430g líquido @ R$50/kg = R$ 21,50
        var calcRes = WeightPriceCalculator.Calculate(450m, 20m, pricePerKg);
        Assert.True(calcRes.IsSuccess);
        Assert.Equal(430m, calcRes.Value.NetWeightGrams);
        Assert.Equal(0.43m, calcRes.Value.NetWeightKilograms);
        Assert.Equal(21.50m, calcRes.Value.TotalPrice.Amount);

        // Peso bruto <= tara deve falhar
        var invalidNet = WeightPriceCalculator.Calculate(20m, 20m, pricePerKg);
        Assert.True(invalidNet.IsFailure);
        Assert.Equal("Weighing.NetWeightMustBePositive", invalidNet.Error.Code);

        // Lançamento manual exige motivo
        var noReason = WeighingRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Açaí Self-Service",
            pricePerKg,
            450m,
            20m,
            WeighingCaptureMode.ManualFallback,
            DateTimeOffset.UtcNow,
            manualFallbackReason: " ");
        Assert.True(noReason.IsFailure);
        Assert.Equal("Weighing.ManualReasonRequired", noReason.Error.Code);

        // Registro válido + vínculo único a pedido
        var rec = WeighingRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Açaí Self-Service",
            pricePerKg,
            450m,
            20m,
            WeighingCaptureMode.ManualFallback,
            DateTimeOffset.UtcNow,
            manualFallbackReason: "Balança desligada").Value;

        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        Assert.True(rec.AttachToOrder(order1).IsSuccess);
        var conflict = rec.AttachToOrder(order2);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Weighing.AlreadyLinked", conflict.Error.Code);
    }
}
