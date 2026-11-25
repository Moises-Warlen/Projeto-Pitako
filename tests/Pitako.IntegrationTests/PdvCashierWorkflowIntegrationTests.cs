using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.Modules.PdvCashier.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class PdvCashierWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 19, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private PdvCashierDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<PdvCashierDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task PdvCashier_FullShiftLifecycle_ShouldPersistRegisterSalesReversalsAndClosingReport()
    {
        var tenantId = Guid.NewGuid();
        var cashier = User.CreateEstablishmentUser(tenantId, "Caixa 1", "cx@loja.com", "hash", EstablishmentRole.Cashier).Value;
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid sessionId;
        Guid sale2Id;

        await using (var db = CreateDb(ctx))
        {
            var svc = new PdvCashierService(new EfCashRegisterRepository(db), new EfPdvSaleRepository(db), ctx, _clock);

            var openRes = await svc.OpenSessionAsync(cashier, "CAIXA-01", 100.00m);
            Assert.True(openRes.IsSuccess);
            sessionId = openRes.Value.Id;

            // Impede abrir segunda sessão simultânea no mesmo terminal
            var dupOpen = await svc.OpenSessionAsync(cashier, "caixa-01", 50.00m);
            Assert.True(dupOpen.IsFailure);
            Assert.Equal("CashRegister.AlreadyOpen", dupOpen.Error.Code);

            await svc.RegisterCashSupplyAsync(cashier, sessionId, 50.00m, "Suprimento início turno");
            await svc.RegisterCashWithdrawalAsync(cashier, sessionId, 30.00m, "Pagamento motoboy avulso");

            // Venda 1: Bruto 110, Desconto 10 autorizado pelo gerente -> Líquido 100 (40 Dinheiro c/ 50 entregues + 60 Pix)
            var sale1 = await svc.SettleSaleAsync(cashier, new SettlePdvSaleRequest(
                sessionId,
                GrossAmount: 110.00m,
                Payments:
                [
                    new PdvPaymentAllocationInput(PaymentMethod.Cash, 40.00m, 50.00m),
                    new PdvPaymentAllocationInput(PaymentMethod.Pix, 60.00m, AuthorizationCode: "PIX-881")
                ],
                OrderId: Guid.NewGuid(),
                DiscountAmount: 10.00m,
                DiscountReason: "Cupom impresso balcão",
                DiscountApprover: manager,
                IdempotencyKey: "pdv-sale-1"));

            Assert.True(sale1.IsSuccess);
            Assert.Equal(10.00m, sale1.Value.Receipt.TotalChangeGiven.Amount);

            // Replay idempotente retorna exatamente o mesmo comprovante sem cobrar 2x
            var replay = await svc.SettleSaleAsync(cashier, new SettlePdvSaleRequest(
                sessionId, 110m, [new PdvPaymentAllocationInput(PaymentMethod.Cash, 100m)], IdempotencyKey: "pdv-sale-1"));
            Assert.Equal(sale1.Value.Sale.Id, replay.Value.Sale.Id);

            // Venda 2 em crédito (R$ 50,00)
            var sale2 = await svc.SettleSaleAsync(cashier, new SettlePdvSaleRequest(
                sessionId, 50.00m, [new PdvPaymentAllocationInput(PaymentMethod.CreditCard, 50.00m)]));
            sale2Id = sale2.Value.Sale.Id;
        }

        // Cancelamento autorizado + fechamento de caixa em outra instância de DbContext
        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new PdvCashierService(new EfCashRegisterRepository(db2), new EfPdvSaleRepository(db2), ctx, _clock);

            var cancelRes = await svc2.CancelSaleAsync(manager, sale2Id, "Item indisponível após cobrança");
            Assert.True(cancelRes.IsSuccess);
            Assert.Equal(PdvSaleStatus.Cancelled, cancelRes.Value.Status);

            // Saldo esperado em espécie: 100 + 50 - 30 + 40 = 160,00. Contado: 155,00 -> Quebra -5,00
            var closeRes = await svc2.CloseSessionAsync(cashier, sessionId, 155.00m, "Fechamento noturno");
            Assert.True(closeRes.IsSuccess);
            Assert.Equal(160.00m, closeRes.Value.ExpectedCashBalance.Amount);
            Assert.Equal(-5.00m, closeRes.Value.CashVariance.Amount);
            Assert.Equal(60.00m, closeRes.Value.NetTotalsByPaymentMethod[PaymentMethod.Pix].Amount);
            Assert.Equal(0.00m, closeRes.Value.NetTotalsByPaymentMethod[PaymentMethod.CreditCard].Amount);

            // Caixa fechado bloqueia novos recebimentos
            var afterClose = await svc2.SettleSaleAsync(cashier, new SettlePdvSaleRequest(
                sessionId, 20.00m, [new PdvPaymentAllocationInput(PaymentMethod.Cash, 20.00m)]));
            Assert.True(afterClose.IsFailure);
            Assert.Equal("CashRegister.SessionClosed", afterClose.Error.Code);
        }
    }
}
