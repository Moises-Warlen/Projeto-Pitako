using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class AcaiBuilderDomainTests
{
    [Fact]
    public void AcaiCupAssemblyCalculator_ShouldEnforceFreeLimitsPerCupSize_AndPreventDuplicateBypass()
    {
        var tenantId = Guid.NewGuid();
        var cup300 = AcaiCupSize.Create(tenantId, "Copo 300 ml", 300, Money.Create(16.00m).Value, freeToppingsLimit: 3, minFreeToppings: 1, AcaiFreeCountingMode.ByPortion).Value;
        var cup500 = AcaiCupSize.Create(tenantId, "Copo 500 ml", 500, Money.Create(22.00m).Value, freeToppingsLimit: 5, minFreeToppings: 0, AcaiFreeCountingMode.ByDistinctItem).Value;

        var leitePo = AcaiFreeTopping.Create(tenantId, "Leite em pó", maxPortionsPerCup: 2).Value;
        var granola = AcaiFreeTopping.Create(tenantId, "Granola", maxPortionsPerCup: 1).Value;
        var pacoca = AcaiFreeTopping.Create(tenantId, "Paçoca", maxPortionsPerCup: 1).Value;
        var banana = AcaiFreeTopping.Create(tenantId, "Banana", maxPortionsPerCup: 1).Value;

        var nutella = AcaiPaidAddon.Create(tenantId, "Nutella", Money.Create(6.00m).Value, maxQuantityPerCup: 2).Value;

        var freeLookup = new Dictionary<Guid, AcaiFreeTopping>
        {
            [leitePo.Id] = leitePo,
            [granola.Id] = granola,
            [pacoca.Id] = pacoca,
            [banana.Id] = banana
        };
        var paidLookup = new Dictionary<Guid, AcaiPaidAddon> { [nutella.Id] = nutella };
        var flavors = new Dictionary<Guid, AcaiBaseFlavor>();

        // Copo 300ml (ByPortion, limite 3): 2x leite em pó + 1x granola = 3 grátis + 1x Nutella (pago) => R$ 22,00
        var valid300 = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup300,
            flavors,
            freeLookup,
            paidLookup,
            new AcaiCupBuildRequest(
                cup300.Id,
                [],
                [new AcaiItemSelection(leitePo.Id, 2), new AcaiItemSelection(granola.Id, 1)],
                [new AcaiItemSelection(nutella.Id, 1)]));

        Assert.True(valid300.IsSuccess);
        Assert.Equal(3, valid300.Value.FreeToppingsUsedCount);
        Assert.Equal(0, valid300.Value.FreeToppingsRemainingCount);
        Assert.Equal(6.00m, valid300.Value.PaidAddonsTotal.Amount);
        Assert.Equal(22.00m, valid300.Value.UnitFinalPrice.Amount);

        // Exceder limite gratuito (4 itens no copo de 3) deve falhar
        var overFree = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup300,
            flavors,
            freeLookup,
            paidLookup,
            new AcaiCupBuildRequest(
                cup300.Id,
                [],
                [
                    new AcaiItemSelection(leitePo.Id, 1),
                    new AcaiItemSelection(granola.Id, 1),
                    new AcaiItemSelection(pacoca.Id, 1),
                    new AcaiItemSelection(banana.Id, 1)
                ],
                []));
        Assert.True(overFree.IsFailure);
        Assert.Equal("Acai.FreeToppingsLimitExceeded", overFree.Error.Code);

        // Tentativa de burlar MaxPortionsPerCup enviando duas entradas separadas do mesmo acompanhamento
        var splitBypass = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup500,
            flavors,
            freeLookup,
            paidLookup,
            new AcaiCupBuildRequest(
                cup500.Id,
                [],
                [new AcaiItemSelection(granola.Id, 1), new AcaiItemSelection(granola.Id, 1)],
                []));
        Assert.True(splitBypass.IsFailure);
        Assert.Equal("Acai.ToppingMaxPortionsExceeded", splitBypass.Error.Code);

        // Sem mínimo obrigatório (min = 1 no copo 300ml) deve falhar
        var underMin = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup300,
            flavors,
            freeLookup,
            paidLookup,
            new AcaiCupBuildRequest(cup300.Id, [], [], []));
        Assert.True(underMin.IsFailure);
        Assert.Equal("Acai.MinFreeToppingsRequired", underMin.Error.Code);

        // Adicional pago acima de MaxQuantityPerCup deve falhar
        var paidOver = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup500,
            flavors,
            freeLookup,
            paidLookup,
            new AcaiCupBuildRequest(
                cup500.Id,
                [],
                [new AcaiItemSelection(leitePo.Id, 2)],
                [new AcaiItemSelection(nutella.Id, 3)]));
        Assert.True(paidOver.IsFailure);
        Assert.Equal("Acai.PaidAddonMaxQuantityExceeded", paidOver.Error.Code);
    }

    [Fact]
    public void AcaiCupAssemblyCalculator_ByDistinctItem_ShouldCountUniqueToppingsAndComputeFlavorsAndMultiCupTotal()
    {
        var tenantId = Guid.NewGuid();
        var cup500 = AcaiCupSize.Create(tenantId, "Copo 500 ml", 500, Money.Create(24.00m).Value, 5, 0, AcaiFreeCountingMode.ByDistinctItem).Value;
        var cupuacu = AcaiBaseFlavor.Create(tenantId, "Creme de Cupuaçu", Money.Create(2.50m).Value).Value;
        var leitePo = AcaiFreeTopping.Create(tenantId, "Leite em pó", maxPortionsPerCup: 2).Value;
        var morango = AcaiFreeTopping.Create(tenantId, "Morango", maxPortionsPerCup: 1).Value;
        var cremeNinho = AcaiPaidAddon.Create(tenantId, "Creme Ninho", Money.Create(5.00m).Value, maxQuantityPerCup: 3).Value;

        var res = AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup500,
            new Dictionary<Guid, AcaiBaseFlavor> { [cupuacu.Id] = cupuacu },
            new Dictionary<Guid, AcaiFreeTopping> { [leitePo.Id] = leitePo, [morango.Id] = morango },
            new Dictionary<Guid, AcaiPaidAddon> { [cremeNinho.Id] = cremeNinho },
            new AcaiCupBuildRequest(
                cup500.Id,
                [cupuacu.Id],
                [new AcaiItemSelection(leitePo.Id, 2), new AcaiItemSelection(morango.Id, 1)],
                [new AcaiItemSelection(cremeNinho.Id, 2)],
                CupQuantity: 2,
                CustomerNotes: "Leite em pó por cima"));

        Assert.True(res.IsSuccess);
        // ByDistinctItem: Leite em pó (2x) + Morango (1x) = 2 gratuitos usados de 5
        Assert.Equal(2, res.Value.FreeToppingsUsedCount);
        Assert.Equal(3, res.Value.FreeToppingsRemainingCount);
        // Unit: 24.00 + 2.50 + 2*5.00 = 36.50; Total x2 = 73.00
        Assert.Equal(36.50m, res.Value.UnitFinalPrice.Amount);
        Assert.Equal(73.00m, res.Value.TotalPrice.Amount);
    }
}
