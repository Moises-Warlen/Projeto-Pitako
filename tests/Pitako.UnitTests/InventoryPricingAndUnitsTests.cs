using Pitako.Modules.Inventory.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class InventoryPricingAndUnitsTests
{
    [Fact]
    public void UnitConverter_ShouldConvertCompatibleUnitsAndRejectIncompatibleDimensions()
    {
        var kgToGrams = UnitConverter.Convert(1.5m, "kg", "g");
        Assert.True(kgToGrams.IsSuccess);
        Assert.Equal(1500m, kgToGrams.Value);

        var mlToLiters = UnitConverter.Convert(250m, "ml", "l");
        Assert.True(mlToLiters.IsSuccess);
        Assert.Equal(0.25m, mlToLiters.Value);

        var invalid = UnitConverter.Convert(1m, "kg", "litro");
        Assert.True(invalid.IsFailure);
        Assert.Equal("Units.Incompatible", invalid.Error.Code);
    }

    [Fact]
    public void SupplyItem_PackageToContentConversionAndPriceHistory_ShouldCalculateExactUsageCost()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

        // Compra 1 caixa com 12 litros de leite a R$ 60,00 -> R$ 5,00/litro -> 200ml = R$ 1,00
        var supplyRes = SupplyItem.Create(
            tenantId,
            "Leite Integral",
            Money.Create(60.00m).Value,
            purchaseQuantity: 1m,
            purchaseUnitCode: "caixa",
            supplierName: "Laticínios Alfa",
            sku: "LEITE-CX12",
            contentQuantityPerPackage: 12m,
            contentUnitCode: "litro",
            createdAtUtc: utc0);

        Assert.True(supplyRes.IsSuccess);
        var supply = supplyRes.Value;
        Assert.Single(supply.PriceHistory);

        var usage200ml = supply.CalculateCostForUsage(200m, "ml");
        Assert.True(usage200ml.IsSuccess);
        Assert.Equal(1.0000m, usage200ml.Value);

        var usageHalfBox = supply.CalculateCostForUsage(0.5m, "cx");
        Assert.True(usageHalfBox.IsSuccess);
        Assert.Equal(30.0000m, usageHalfBox.Value);

        var updateRes = supply.UpdatePurchasePrice(Money.Create(72.00m).Value, "Laticínios Beta", "Reajuste sazonal", utc0.AddDays(1), Guid.NewGuid());
        Assert.True(updateRes.IsSuccess);
        Assert.Equal(2, supply.PriceHistory.Count);
        Assert.Equal(1.2000m, supply.CalculateCostForUsage(200m, "ml").Value);
    }

    [Fact]
    public void TechnicalSheet_And_PriceFormationCalculator_ShouldComputeCostsAndMargins()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

        // Caixa de açaí 10 kg a R$ 150,00 -> R$ 15,00/kg -> 300 g = R$ 4,50
        var acai = SupplyItem.Create(tenantId, "Polpa de Açaí", Money.Create(150.00m).Value, 1m, "caixa", contentQuantityPerPackage: 10m, contentUnitCode: "kg", createdAtUtc: utc0).Value;
        // Leite em pó 400 g a R$ 16,00 -> 50 g = R$ 2,00
        var leitePo = SupplyItem.Create(tenantId, "Leite em Pó", Money.Create(16.00m).Value, 400m, "g", createdAtUtc: utc0).Value;
        // Copo + tampa R$ 1,50 (pacote com 100 unidades a R$ 150,00 -> 1 unidade = R$ 1,50)
        var copo = SupplyItem.Create(tenantId, "Copo 500ml c/ Tampa", Money.Create(150.00m).Value, 1m, "pacote", contentQuantityPerPackage: 100m, contentUnitCode: "unidade", createdAtUtc: utc0).Value;

        var customBalde = TenantCustomUnit.Create(tenantId, "balde", "Balde 5L", UnitDimension.Volume, 5000m).Value;
        var customLookup = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal) { [customBalde.Code] = customBalde.ToDefinition() };

        var lookup = new Dictionary<Guid, SupplyItem>
        {
            [acai.Id] = acai,
            [leitePo.Id] = leitePo,
            [copo.Id] = copo
        };

        var sheetRes = TechnicalSheet.Create(
            tenantId,
            "Copo Açaí 500ml",
            yieldQuantity: 1m,
            yieldUnitCode: "unidade",
            ingredients:
            [
                new IngredientInput(acai.Id, 300m, "g"),
                new IngredientInput(leitePo.Id, 50m, "g")
            ],
            packagings:
            [
                new PackagingInput(copo.Id, "Copo + Tampa", 1m, "un")
            ],
            lookup,
            customLookup,
            utc0);

        Assert.True(sheetRes.IsSuccess);
        var sheet = sheetRes.Value;
        Assert.Equal(6.5000m, sheet.IngredientsTotalCost);
        Assert.Equal(1.5000m, sheet.PackagingsTotalCost);
        Assert.Equal(8.0000m, sheet.TotalCost);
        Assert.Equal(8.0000m, sheet.CostPerPortion);
        Assert.Equal(1, sheet.Version);
        Assert.Single(sheet.CostHistory);

        // Formação por margem líquida desejada:
        // Deduções: 10% imposto + 15% marketplace + 5% operacional = 30%. Margem desejada = 20%.
        // Total = 50% -> Preço sugerido = 8.00 / 0.50 = 16.00; Break-even = 8.00 / 0.70 = 11.43
        var pricingRes = PriceFormationCalculator.Calculate(new PriceFormationInput(
            sheet.CostPerPortion,
            TaxPercent: 10m,
            ChannelFeePercent: 15m,
            OperationalOverheadPercent: 5m,
            DesiredNetMarginPercent: 20m));

        Assert.True(pricingRes.IsSuccess);
        Assert.Equal(11.43m, pricingRes.Value.BreakEvenPrice.Amount);
        Assert.Equal(16.00m, pricingRes.Value.SuggestedSellingPrice.Amount);
        Assert.Equal(2.0000m, pricingRes.Value.EffectiveMarkupMultiplier);
        Assert.Equal(3.20m, pricingRes.Value.EstimatedNetProfit.Amount);
        Assert.Equal(20.00m, pricingRes.Value.EstimatedNetMarginPercent);

        // Formação por markup explícito (2.5x sobre R$ 8,00 = R$ 20,00)
        var markupRes = PriceFormationCalculator.Calculate(new PriceFormationInput(
            sheet.CostPerPortion,
            TaxPercent: 10m,
            ChannelFeePercent: 15m,
            OperationalOverheadPercent: 5m,
            DesiredNetMarginPercent: 0m,
            ExplicitMarkupMultiplier: 2.5m));
        Assert.True(markupRes.IsSuccess);
        Assert.Equal(20.00m, markupRes.Value.SuggestedSellingPrice.Amount);

        // Soma de deduções + margem >= 100% deve falhar
        var impossible = PriceFormationCalculator.Calculate(new PriceFormationInput(8m, 40m, 30m, 10m, 20m));
        Assert.True(impossible.IsFailure);
        Assert.Equal("Pricing.ImpossibleMargin", impossible.Error.Code);
    }
}
