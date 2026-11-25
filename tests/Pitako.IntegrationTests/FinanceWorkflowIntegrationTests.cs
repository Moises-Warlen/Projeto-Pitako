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
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class TestTokenizedGatewayClient : IPaymentGatewayClient
{
    public Task<Result<ExternalGatewayChargeResponse>> ProcessTokenizedChargeAsync(
        PaymentGatewayConfig config,
        string decryptedSecret,
        ExternalGatewayChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        var fee = decimal.Round(request.GrossAmount * (config.DefaultMdrPercent / 100m), 2, MidpointRounding.ToEven);
        return Task.FromResult(Result.Success(new ExternalGatewayChargeResponse(
            $"TX-{request.OrderOrSaleReference}",
            "NSU987654",
            fee)));
    }
}

public sealed class FinanceWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 59, 0, TimeSpan.Zero));
    private readonly FinanceAesGcmSecretProtector _protector = FinanceAesGcmSecretProtector.FromPassphrase("pitako-fin-key");

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
    public async Task Finance_Entries_CashFlow_Dre_PciGateway_Reconciliation_AndOfflineFail_ShouldWorkCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var financial = User.CreateEstablishmentUser(tenantId, "Financeiro", "fin@loja.com", "hash", EstablishmentRole.Financial).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var tStart = _clock.UtcNow.AddDays(-5);
        var tEnd = _clock.UtcNow.AddDays(5);

        await using (var db = CreateDb(ctx))
        {
            var svc = new FinanceManagementService(
                new EfFinancialEntryRepository(db),
                new EfCashFlowLedgerRepository(db),
                new EfPaymentGatewayConfigRepository(db),
                new EfPaymentTransactionRepository(db),
                new EfReconciliationBatchRepository(db),
                _protector,
                new TestTokenizedGatewayClient(),
                ctx,
                _clock);

            // 1. Contas a receber e pagar + baixas no Fluxo de Caixa + DRE
            var cr1 = (await svc.CreateEntryAsync(financial, FinancialEntryKind.Receivable, DreLineCategory.GrossSalesRevenue, "iFood Repasse", "Lote semanal", 1000.00m, _clock.UtcNow)).Value;
            await svc.SettleEntryAsync(financial, cr1.Id, 900.00m, 100.00m, "TED Repasse");

            var cp1 = (await svc.CreateEntryAsync(financial, FinancialEntryKind.Payable, DreLineCategory.CogsSuppliesAndInputs, "Atacado Açaí", "Caixas polpa", 400.00m, _clock.UtcNow)).Value;
            await svc.SettleEntryAsync(financial, cp1.Id, 400.00m, 0m, "PIX");

            await svc.CreateEntryAsync(financial, FinancialEntryKind.Payable, DreLineCategory.RentAndUtilities, "Energia Elétrica", "Conta em aberto", 150.00m, _clock.UtcNow.AddDays(2));

            var cashFlow = (await svc.GetCashFlowSummaryAsync(financial, tStart, tEnd)).Value;
            Assert.Equal(900.00m, cashFlow.RealizedInflows);
            Assert.Equal(400.00m, cashFlow.RealizedOutflows);
            Assert.Equal(500.00m, cashFlow.NetRealizedCashFlow);
            Assert.Equal(150.00m, cashFlow.PendingPayables);
            Assert.Equal(350.00m, cashFlow.ProjectedNetBalance);

            var dre = (await svc.GetDreStatementAsync(financial, tStart, tEnd)).Value;
            Assert.Equal(1000.00m, dre.GrossRevenue);
            Assert.Equal(100.00m, dre.DeductionsAndFees);
            Assert.Equal(900.00m, dre.NetRevenue);
            Assert.Equal(400.00m, dre.CogsAmount);
            Assert.Equal(150.00m, dre.OperatingExpenses);
            Assert.Equal(350.00m, dre.OperatingResult);

            // 2. Gateway Stone cifrado + cobrança tokenizada sem PAN + conciliação
            var gw = (await svc.ConfigurePaymentGatewayAsync(financial, PaymentGatewayProvider.Stone, "STONE-M01", "stone-secret-key", defaultMdrPercent: 3.00m)).Value;
            Assert.StartsWith("v1:", gw.EncryptedSecretBlob, StringComparison.Ordinal);

            var rawPanAttempt = await svc.ProcessGatewayChargeAsync(financial, gw.Id, new ExternalGatewayChargeRequest("ORD-99", 100.00m, "Visa", "1234", "4532012345678901"));
            Assert.True(rawPanAttempt.IsFailure);
            Assert.Equal("Finance.RawCardDataForbidden", rawPanAttempt.Error.Code);

            var tx1 = (await svc.ProcessGatewayChargeAsync(financial, gw.Id, new ExternalGatewayChargeRequest("ORD-01", 100.00m, "Visa", "4455", "tok_visa_abc"))).Value;
            Assert.Equal(97.00m, tx1.NetAmount.Amount);
            Assert.Equal("4455", tx1.CardLast4Digits);

            var recon = (await svc.RunSettlementReconciliationAsync(financial, "Stone",
            [
                new StatementSettlementRow("TX-ORD-01", 97.00m),
                new StatementSettlementRow("TX-EXT-99", 25.00m)
            ])).Value;

            Assert.Equal(1, recon.ExactMatchesCount);
            Assert.Equal(1, recon.DivergencesCount);
            Assert.Equal(25.00m, recon.TotalVarianceAmount.Amount);
        }

        // 3. Gateway sem credenciais em produção falha explicitamente sem simular autorização
        await using (var dbOff = CreateDb(ctx))
        {
            var svcOff = new FinanceManagementService(
                new EfFinancialEntryRepository(dbOff),
                new EfCashFlowLedgerRepository(dbOff),
                new EfPaymentGatewayConfigRepository(dbOff),
                new EfPaymentTransactionRepository(dbOff),
                new EfReconciliationBatchRepository(dbOff),
                _protector,
                new UnavailablePaymentGatewayClient(),
                ctx,
                _clock);

            var gwOff = (await svcOff.ConfigurePaymentGatewayAsync(financial, PaymentGatewayProvider.MercadoPago, "MP-01", "mp-secret", 2.50m)).Value;
            var failCharge = await svcOff.ProcessGatewayChargeAsync(financial, gwOff.Id, new ExternalGatewayChargeRequest("ORD-02", 50.00m, "PIX", null, "tok_pix"));
            Assert.True(failCharge.IsFailure);
            Assert.Equal("Finance.GatewayCredentialsNotConfigured", failCharge.Error.Code);
        }
    }
}
