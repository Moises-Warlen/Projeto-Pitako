using Pitako.Modules.Finance.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class FinanceDomainTests
{
    [Fact]
    public void PciCardDataGuard_ShouldBlockRawPan_AndAllowLast4Only()
    {
        var blocked = PciCardDataGuard.EnsureNoRawCardData("Pedido #1", "4532 0123 4567 8901");
        Assert.True(blocked.IsFailure);
        Assert.Equal("Finance.RawCardDataForbidden", blocked.Error.Code);

        var okLast4 = PciCardDataGuard.ValidateLast4Digits("8901");
        Assert.True(okLast4.IsSuccess);
        Assert.Equal("8901", okLast4.Value);
    }

    [Fact]
    public void DreCalculator_And_ReconciliationBatch_ShouldComputeExactAccountingFigures()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var utc1 = new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero);

        var sale = FinancialEntry.Create(tenantId, "CR-1", FinancialEntryKind.Receivable, DreLineCategory.GrossSalesRevenue, "Clientes", "Vendas mês", 1000.00m, utc0.AddDays(5)).Value;
        sale.RecordSettlement(Money.Create(950.00m).Value, Money.Create(50.00m).Value, "Cartão", utc0.AddDays(6));

        var cmv = FinancialEntry.Create(tenantId, "CP-1", FinancialEntryKind.Payable, DreLineCategory.CogsSuppliesAndInputs, "Distribuidora", "Açaí e Leite", 350.00m, utc0.AddDays(10)).Value;
        var rent = FinancialEntry.Create(tenantId, "CP-2", FinancialEntryKind.Payable, DreLineCategory.RentAndUtilities, "Imobiliária", "Aluguel", 200.00m, utc0.AddDays(15)).Value;

        var dre = DreCalculator.Compute(tenantId, utc0, utc1, [sale, cmv, rent]);
        Assert.Equal(1000.00m, dre.GrossRevenue);
        Assert.Equal(50.00m, dre.DeductionsAndFees);
        Assert.Equal(950.00m, dre.NetRevenue);
        Assert.Equal(350.00m, dre.CogsAmount);
        Assert.Equal(600.00m, dre.GrossProfit);
        Assert.Equal(200.00m, dre.OperatingExpenses);
        Assert.Equal(400.00m, dre.OperatingResult);

        var batch = SettlementReconciliationBatch.Start(tenantId, "CONC-0001", "Stone", utc1);
        batch.AddLine("TX-1", 95.00m, 95.00m, true);
        batch.AddLine("TX-2", 95.00m, 92.50m, true);
        batch.AddLine("TX-3", 0m, 50.00m, false);

        Assert.Equal(1, batch.ExactMatchesCount);
        Assert.Equal(2, batch.DivergencesCount);
        Assert.Equal(52.50m, batch.TotalVarianceAmount.Amount);
    }
}
