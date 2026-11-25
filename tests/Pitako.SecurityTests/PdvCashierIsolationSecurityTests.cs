using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.Modules.PdvCashier.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class PdvCashierIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

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
    public async Task TenantA_ShouldNeverReadSettleCancelOrApprove_PdvOfTenantB_AndRbacLeastPrivilegeMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var cashierA = User.CreateEstablishmentUser(tenantA, "Caixa A", "cx@a.com", "hash", EstablishmentRole.Cashier).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid sessionBId;
        Guid saleBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new PdvCashierService(new EfCashRegisterRepository(dbB), new EfPdvSaleRepository(dbB), ctxB, _clock);
            sessionBId = (await svcB.OpenSessionAsync(adminB, "CAIXA-01", 100m)).Value.Id;
            saleBId = (await svcB.SettleSaleAsync(adminB, new SettlePdvSaleRequest(
                sessionBId, 50m, [new PdvPaymentAllocationInput(PaymentMethod.Cash, 50m)]))).Value.Sale.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new PdvCashierService(new EfCashRegisterRepository(dbA), new EfPdvSaleRepository(dbA), ctxA, _clock);

            // Global query filter oculta sessões, movimentos, vendas e parcelas da Loja B
            Assert.Empty(await dbA.CashRegisterSessions.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.CashMovements.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.PdvSales.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.PdvSalePayments.AsNoTracking().ToListAsync());

            // Admin A não consegue fechar caixa nem cancelar venda da Loja B
            var closeB = await svcA.CloseSessionAsync(adminA, sessionBId, 0m);
            Assert.True(closeB.IsFailure);
            Assert.Equal("CashRegister.NotFound", closeB.Error.Code);

            var cancelB = await svcA.CancelSaleAsync(adminA, saleBId, "Tentativa cruzada");
            Assert.True(cancelB.IsFailure);
            Assert.Equal("PdvSale.NotFound", cancelB.Error.Code);

            // Loja A abre seu próprio caixa "CAIXA-01" independentemente da Loja B
            var sessionA = (await svcA.OpenSessionAsync(cashierA, "CAIXA-01", 80m)).Value;

            // Caixa A não pode conceder desconto por conta própria (sem Approve)
            var selfDiscount = await svcA.SettleSaleAsync(cashierA, new SettlePdvSaleRequest(
                sessionA.Id, 50m, [new PdvPaymentAllocationInput(PaymentMethod.Cash, 45m)],
                DiscountAmount: 5m, DiscountReason: "Auto desconto"));
            Assert.True(selfDiscount.IsFailure);
            Assert.Equal("PdvSale.DiscountApprovalRequired", selfDiscount.Error.Code);

            // Aprovador de outro tenant (adminB) dispara CrossTenantAccessException
            await Assert.ThrowsAsync<CrossTenantAccessException>(() =>
                svcA.SettleSaleAsync(cashierA, new SettlePdvSaleRequest(
                    sessionA.Id, 50m, [new PdvPaymentAllocationInput(PaymentMethod.Cash, 45m)],
                    DiscountAmount: 5m, DiscountReason: "Aprovador externo", DiscountApprover: adminB)));

            // Cozinha A não pode abrir caixa nem receber pagamentos
            var kitchenOpen = await svcA.OpenSessionAsync(kitchenA, "CAIXA-02", 50m);
            Assert.True(kitchenOpen.IsFailure);
            Assert.Equal("Pdv.Forbidden", kitchenOpen.Error.Code);
        }
    }
}
