using Pitako.Modules.Inventory.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class InventoryStockAndProductionTests
{
    [Fact]
    public void SupplyItem_ConvertQuantity_ShouldConvertAcrossPackageAndControlUnits()
    {
        var tenantId = Guid.NewGuid();
        var polpa = SupplyItem.Create(
            tenantId,
            "Polpa Açaí",
            Money.Create(150.00m).Value,
            1m,
            "caixa",
            contentQuantityPerPackage: 10m,
            contentUnitCode: "kg").Value;

        var twoBoxesInGrams = polpa.ConvertQuantity(2m, "cx", "g");
        Assert.True(twoBoxesInGrams.IsSuccess);
        Assert.Equal(20000m, twoBoxesInGrams.Value);

        var recipeGramsToKg = polpa.ConvertQuantity(500m, "g", "kg");
        Assert.True(recipeGramsToKg.IsSuccess);
        Assert.Equal(0.5m, recipeGramsToKg.Value);
    }

    [Fact]
    public void StockBalance_ShouldConsumeLotsInFefoOrder_BlockExpiredAndNegative_AndRespectIdempotency()
    {
        var tenantId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var leite = SupplyItem.Create(tenantId, "Leite", Money.Create(5.00m).Value, 1m, "litro", createdAtUtc: utcNow).Value;

        var balance = StockBalance.Create(
            tenantId,
            leite,
            "litro",
            minimumStockQuantity: 3m,
            allowNegativeStock: false,
            autoDeductOnOrder: true,
            blockExpiredLots: true).Value;

        // Lote vencido ontem (não pode sair em consumo normal)
        balance.RegisterEntry(2m, 4.50m, StockMovementType.PurchaseEntry, "Compra Lote Vencido", utcNow, null, "LOT-EXP", null, utcNow.AddDays(-1)).Value.ToString();
        // Lote que vence em 2 dias (sai primeiro - FEFO)
        balance.RegisterEntry(2m, 5.00m, StockMovementType.PurchaseEntry, "Compra Lote Curto", utcNow, null, "LOT-CURTO", null, utcNow.AddDays(2));
        // Lote que vence em 10 dias (sai depois)
        balance.RegisterEntry(4m, 6.00m, StockMovementType.PurchaseEntry, "Compra Lote Longo", utcNow, null, "LOT-LONGO", null, utcNow.AddDays(10));

        Assert.Equal(8m, balance.QuantityOnHand);

        // Consumo de 3 litros deve drenar 2L de LOT-CURTO (R$5) + 1L de LOT-LONGO (R$6) = R$16 total
        var consumeRes = balance.RegisterConsumption(3m, 5m, StockMovementType.ProductionConsumption, "Produção", utcNow, null, idempotencyKey: "prod-1");
        Assert.True(consumeRes.IsSuccess);
        Assert.Equal(16.0000m, consumeRes.Value.TotalCost);
        Assert.Equal(5m, balance.QuantityOnHand);

        var lotExp = balance.Lots.Single(l => l.BatchNumber == "LOT-EXP");
        var lotCurto = balance.Lots.Single(l => l.BatchNumber == "LOT-CURTO");
        var lotLongo = balance.Lots.Single(l => l.BatchNumber == "LOT-LONGO");
        Assert.Equal(2m, lotExp.RemainingQuantity);
        Assert.Equal(0m, lotCurto.RemainingQuantity);
        Assert.Equal(3m, lotLongo.RemainingQuantity);

        // Idempotência: mesma chave não deduz de novo
        var replay = balance.RegisterConsumption(3m, 5m, StockMovementType.ProductionConsumption, "Produção", utcNow, null, idempotencyKey: "prod-1");
        Assert.True(replay.IsSuccess);
        Assert.Equal(5m, balance.QuantityOnHand);

        // Consumo de 4L válidos falha porque 2L do saldo de 5L estão vencidos (sobram só 3L válidos)
        var overExpired = balance.RegisterConsumption(4m, 5m, StockMovementType.OrderSaleDeduction, "Pedido", utcNow, null);
        Assert.True(overExpired.IsFailure);
        Assert.Equal("Stock.OnlyExpiredLotsAvailable", overExpired.Error.Code);

        // Baixa por perda pode dar baixa no lote vencido e aciona alerta de estoque mínimo (5 - 3 = 2 <= 3)
        var wasteRes = balance.RegisterConsumption(3m, 5m, StockMovementType.LossOrWaste, "Descarte vencido", utcNow, null, allowExpiredForWaste: true);
        Assert.True(wasteRes.IsSuccess);
        Assert.Equal(2m, balance.QuantityOnHand);
        Assert.True(balance.IsBelowMinimum());

        // Sem estoque suficiente para baixa sem AllowNegativeStock
        var negAttempt = balance.RegisterConsumption(5m, 5m, StockMovementType.OrderSaleDeduction, "Pedido", utcNow, null);
        Assert.True(negAttempt.IsFailure);
        Assert.Equal("Stock.InsufficientBalance", negAttempt.Error.Code);

        // Quando AllowNegativeStock = true, permite saldo negativo
        balance.UpdatePolicy(3m, allowNegativeStock: true, autoDeductOnOrder: true, blockExpiredLots: true);
        var negAllowed = balance.RegisterConsumption(5m, 5m, StockMovementType.OrderSaleDeduction, "Pedido contingência", utcNow, null);
        Assert.True(negAllowed.IsSuccess);
        Assert.Equal(-3m, balance.QuantityOnHand);
    }
}
