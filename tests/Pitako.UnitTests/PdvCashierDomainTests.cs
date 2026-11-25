using Pitako.Modules.PdvCashier.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class PdvCashierDomainTests
{
    [Fact]
    public void CashRegister_MultiPayment_Change_SupplyWithdrawal_CancelAndVariance_ShouldComputeExactTotals()
    {
        var tenantId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero);

        var session = CashRegisterSession.Open(tenantId, "caixa-01", cashierId, Money.Create(100.00m).Value, utc0).Value;
        Assert.True(session.RegisterCashSupply(Money.Create(50.00m).Value, "Reforço troco", cashierId, utc0.AddMinutes(5)).IsSuccess);
        Assert.True(session.RegisterCashWithdrawal(Money.Create(20.00m).Value, "Compra gelo", cashierId, utc0.AddMinutes(10)).IsSuccess);

        // Sangria maior que saldo em espécie deve falhar (100 + 50 - 20 = 130)
        var overWithdrawal = session.RegisterCashWithdrawal(Money.Create(150.00m).Value, "Excesso", cashierId, utc0.AddMinutes(11));
        Assert.True(overWithdrawal.IsFailure);
        Assert.Equal("CashRegister.InsufficientCashForWithdrawal", overWithdrawal.Error.Code);

        // Desconto sem autorizador deve falhar
        var unauthorizedDiscount = PdvSale.CreateAndSettle(
            session, null, "PDV-001", Money.Create(100m).Value, Money.Create(10m).Value, "Cortesia", null,
            [new PdvPaymentAllocationInput(PaymentMethod.Cash, 90m)], cashierId, utc0.AddMinutes(12));
        Assert.True(unauthorizedDiscount.IsFailure);
        Assert.Equal("PdvSale.DiscountAuthorizationRequired", unauthorizedDiscount.Error.Code);

        // Venda múltipla: Bruto 110 - Desc 10 = Líquido 100 (Dinheiro R$40 c/ R$50 entregues -> troco R$10 + Pix R$60)
        var saleRes = PdvSale.CreateAndSettle(
            session, Guid.NewGuid(), "PDV-000001", Money.Create(110m).Value, Money.Create(10m).Value, "Fidelidade", managerId,
            [
                new PdvPaymentAllocationInput(PaymentMethod.Cash, 40m, 50m),
                new PdvPaymentAllocationInput(PaymentMethod.Pix, 60m)
            ],
            cashierId, utc0.AddMinutes(15));

        Assert.True(saleRes.IsSuccess);
        var receipt = saleRes.Value.BuildReceiptSnapshot();
        Assert.Equal(10.00m, receipt.TotalChangeGiven.Amount);
        // Esperado em caixa espécie: 100 + 50 - 20 + 40 = 170,00
        Assert.Equal(170.00m, session.ExpectedCashBalance.Amount);
        Assert.Equal(60.00m, session.GetNetTotalByMethod(PaymentMethod.Pix).Amount);

        // Segunda venda paga integral no Dinheiro (R$30) e depois cancelada -> estorna R$30
        var sale2 = PdvSale.CreateAndSettle(
            session, null, "PDV-000002", Money.Create(30m).Value, Money.Zero(), null, null,
            [new PdvPaymentAllocationInput(PaymentMethod.Cash, 30m, 30m)], cashierId, utc0.AddMinutes(20)).Value;
        Assert.Equal(200.00m, session.ExpectedCashBalance.Amount);

        Assert.True(sale2.CancelAndReverseInSession(session, managerId, "Erro de lançamento", utc0.AddMinutes(25)).IsSuccess);
        Assert.Equal(170.00m, session.ExpectedCashBalance.Amount);

        // Fechamento conferido com R$ 168,00 -> quebra de R$ -2,00
        var closeRes = session.Close(Money.Create(168.00m).Value, cashierId, "Diferença de troco", utc0.AddHours(4));
        Assert.True(closeRes.IsSuccess);
        Assert.Equal(-2.00m, session.CashVariance!.Amount);
    }
}
