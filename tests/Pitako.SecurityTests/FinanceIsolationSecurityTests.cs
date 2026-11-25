using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Application.Services;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.Finance.Infrastructure.Gateways;
using Pitako.Modules.Finance.Infrastructure.Persistence;
using Pitako.Modules.Finance.Infrastructure.Security;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class FinanceIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly FinanceAesGcmSecretProtector _protector = FinanceAesGcmSecretProtector.FromPassphrase("pitako-fin-sec");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private FinanceDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverAccess_TenantB_FinanceRecords_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var financialA = User.CreateEstablishmentUser(tenantA, "Fin A", "fin@a.com", "hash", EstablishmentRole.Financial).Value;
        var cashierA = User.CreateEstablishmentUser(tenantA, "Caixa A", "cx@a.com", "hash", EstablishmentRole.Cashier).Value;
        var waiterA = User.CreateEstablishmentUser(tenantA, "Garçom A", "gar@a.com", "hash", EstablishmentRole.Waiter).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var offlineClient = new UnavailablePaymentGatewayClient();
        Guid entryBId;
        Guid gatewayBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new FinanceManagementService(
                new EfFinancialEntryRepository(dbB),
                new EfCashFlowLedgerRepository(dbB),
                new EfPaymentGatewayConfigRepository(dbB),
                new EfPaymentTransactionRepository(dbB),
                new EfReconciliationBatchRepository(dbB),
                _protector,
                offlineClient,
                ctxB,
                _clock);

            var entB = (await svcB.CreateEntryAsync(adminB, FinancialEntryKind.Receivable, DreLineCategory.GrossSalesRevenue, "Cli B", "Venda B", 200m, _clock.UtcNow)).Value;
            entryBId = entB.Id;
            await svcB.SettleEntryAsync(adminB, entryBId, 200m, 0m, "PIX");
            var gwB = (await svcB.ConfigurePaymentGatewayAsync(adminB, PaymentGatewayProvider.Stone, "B-ST", "sec-b", 2m)).Value;
            gatewayBId = gwB.Id;
            await svcB.RunSettlementReconciliationAsync(adminB, "Stone", [new StatementSettlementRow("TX-B", 10m)]);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new FinanceManagementService(
            new EfFinancialEntryRepository(dbA),
            new EfCashFlowLedgerRepository(dbA),
            new EfPaymentGatewayConfigRepository(dbA),
            new EfPaymentTransactionRepository(dbA),
            new EfReconciliationBatchRepository(dbA),
            _protector,
            offlineClient,
            ctxA,
            _clock);

        // Global Query Filters isolam todas as 6 tabelas de Finance
        Assert.Empty(await dbA.FinancialEntries.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.CashFlowLedger.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.PaymentGatewayConfigs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.PaymentTransactions.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.ReconciliationBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.ReconciliationLines.AsNoTracking().ToListAsync());

        // Cross-tenant settle e gateway falham
        var crossSettle = await svcA.SettleEntryAsync(financialA, entryBId, 10m, 0m, "PIX");
        Assert.True(crossSettle.IsFailure);
        Assert.Equal("Finance.EntryNotFound", crossSettle.Error.Code);

        var crossCharge = await svcA.ProcessGatewayChargeAsync(financialA, gatewayBId, new ExternalGatewayChargeRequest("ORD-X", 10m, "PIX", null, "tok_x"));
        Assert.True(crossCharge.IsFailure);
        Assert.Equal("Finance.GatewayNotFound", crossCharge.Error.Code);

        // RBAC menor privilégio: Caixa e Garçom não acessam módulo Financeiro
        var cashierTry = await svcA.GetDreStatementAsync(cashierA, _clock.UtcNow.AddDays(-1), _clock.UtcNow);
        Assert.True(cashierTry.IsFailure);
        Assert.Equal("Finance.Forbidden", cashierTry.Error.Code);

        var waiterTry = await svcA.CreateEntryAsync(waiterA, FinancialEntryKind.Payable, DreLineCategory.MarketingAndOtherOpex, "X", "Y", 10m, _clock.UtcNow);
        Assert.True(waiterTry.IsFailure);
        Assert.Equal("Finance.Forbidden", waiterTry.Error.Code);
    }
}
