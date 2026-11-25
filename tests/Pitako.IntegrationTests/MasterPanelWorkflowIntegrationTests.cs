using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.MasterPanel.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class MasterPanelWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 24, 22, 0, 0, TimeSpan.Zero));
    private readonly User _masterUser = User.CreateSaasUser("Master", "master@pitako.com", "hash", SaasRole.MasterAdministrator).Value;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private MasterPanelDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MasterPanelDbContext>().UseSqlite(_connection).Options;
        return new MasterPanelDbContext(options, _clock);
    }

    private sealed class MasterScope(
        MasterPlanAndSubscriptionService plans,
        PlanEntitlementEnforcementService enforcement,
        MasterOperationsAndDashboardService ops,
        MasterPanelDbContext db) : IDisposable
    {
        public MasterPlanAndSubscriptionService Plans { get; } = plans;
        public PlanEntitlementEnforcementService Enforcement { get; } = enforcement;
        public MasterOperationsAndDashboardService Ops { get; } = ops;
        public void Dispose() => db.Dispose();
    }

    private MasterScope CreateScope()
    {
        var db = CreateDb();
        var planRepo = new EfCommercialPlanRepository(db);
        var customerRepo = new EfMasterCustomerRepository(db);
        var subRepo = new EfSubscriptionRepository(db);
        var ticketRepo = new EfSupportTicketRepository(db);
        var settingRepo = new EfGlobalSettingRepository(db);

        return new MasterScope(
            new MasterPlanAndSubscriptionService(planRepo, customerRepo, subRepo, _clock),
            new PlanEntitlementEnforcementService(subRepo, planRepo, _clock),
            new MasterOperationsAndDashboardService(customerRepo, subRepo, planRepo, ticketRepo, settingRepo, _clock),
            db);
    }

    [Fact]
    public async Task Plans_Enforcement_Unlimited_UpgradeDowngrade_And_Delinquency_ShouldWorkEndToEnd()
    {
        Guid basicPlanId;
        Guid unlimitedPlanId;
        Guid customerId;
        Guid subscriptionId;

        using (var setup = CreateScope())
        {
            var basic = await setup.Plans.CreatePlanAsync(_masterUser, new CreatePlanCommand(
                "basico", "Básico", "1 loja", 99.90m, "BRL", BillingCycle.Monthly, 7,
                new PlanQuotaConfiguration(
                    QuotaLimit.Limited(1).Value,
                    QuotaLimit.Limited(1).Value,
                    QuotaLimit.Limited(1).Value,
                    QuotaLimit.Limited(3).Value,
                    QuotaLimit.Limited(2).Value,
                    QuotaLimit.Limited(2).Value,
                    QuotaLimit.Limited(500).Value),
                [SystemModules.Pdv, SystemModules.Catalog]));
            Assert.True(basic.IsSuccess);
            basicPlanId = basic.Value.Id;

            var unlimited = await setup.Plans.CreatePlanAsync(_masterUser, new CreatePlanCommand(
                "ilimitado", "Ilimitado", "Sem limite comercial", 699.90m, "BRL", BillingCycle.Monthly, 14,
                new PlanQuotaConfiguration(
                    QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(),
                    QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited()),
                [SystemModules.Pdv, SystemModules.Catalog, SystemModules.WhatsApp, SystemModules.Marketplaces, SystemModules.Acai]));
            Assert.True(unlimited.IsSuccess);
            unlimitedPlanId = unlimited.Value.Id;

            var created = await setup.Plans.CreateCustomerSubscriptionAsync(
                _masterUser, "Rede Açaí Top", "11222333000181", "financeiro@acaitop.com", "11999999999",
                basicPlanId, startWithTrial: false);
            Assert.True(created.IsSuccess);
            customerId = created.Value.Customer.Id;
            subscriptionId = created.Value.Subscription.Id;
        }

        // 1. No Básico: 1ª loja permitida, 2ª bloqueada com sugestão de upgrade; módulo WhatsApp bloqueado
        using (var checkBasic = CreateScope())
        {
            var allowFirst = await checkBasic.Enforcement.CheckResourceAllocationAsync(customerId, QuotaResourceKind.Establishments, currentUsage: 0);
            Assert.True(allowFirst.IsSuccess);

            var blockSecond = await checkBasic.Enforcement.CheckResourceAllocationAsync(customerId, QuotaResourceKind.Establishments, currentUsage: 1);
            Assert.True(blockSecond.IsFailure);
            Assert.Equal("Plan.LimitExceeded", blockSecond.Error.Code);
            Assert.Contains("Ilimitado", blockSecond.Error.Message);

            var pdvOk = await checkBasic.Enforcement.CheckModuleAccessAsync(customerId, SystemModules.Pdv);
            Assert.True(pdvOk.IsSuccess);

            var whatsAppBlocked = await checkBasic.Enforcement.CheckModuleAccessAsync(customerId, SystemModules.WhatsApp);
            Assert.True(whatsAppBlocked.IsFailure);
            Assert.Equal("Plan.ModuleNotIncluded", whatsAppBlocked.Error.Code);
        }

        // 2. Upgrade para Plano Ilimitado libera escala sem limite artificial e novos módulos
        using (var upgrade = CreateScope())
        {
            var res = await upgrade.Plans.ChangeSubscriptionPlanAsync(
                _masterUser, subscriptionId, unlimitedPlanId,
                new CurrentUsageSnapshot(1, 1, 1, 2, 1, 1));
            Assert.True(res.IsSuccess);

            var hugeScale = await upgrade.Enforcement.CheckResourceAllocationAsync(customerId, QuotaResourceKind.Establishments, currentUsage: 500_000);
            Assert.True(hugeScale.IsSuccess);
            Assert.True(hugeScale.Value.IsUnlimited);
            Assert.Null(hugeScale.Value.MaxAllowed);

            var whatsAppOk = await upgrade.Enforcement.CheckModuleAccessAsync(customerId, SystemModules.WhatsApp);
            Assert.True(whatsAppOk.IsSuccess);

            // Tentativa de downgrade para Básico com 4 lojas em uso deve falhar
            var badDowngrade = await upgrade.Plans.ChangeSubscriptionPlanAsync(
                _masterUser, subscriptionId, basicPlanId,
                new CurrentUsageSnapshot(4, 4, 2, 5, 3, 2));
            Assert.True(badDowngrade.IsFailure);
            Assert.Equal("Plan.DowngradeBlockedByCurrentUsage", badDowngrade.Error.Code);
        }

        // 3. Faturamento, inadimplência, suspensão automática, bloqueio operacional e reativação via pagamento
        Guid invoiceId;
        using (var billing = CreateScope())
        {
            var inv = await billing.Plans.IssueInvoiceAsync(_masterUser, subscriptionId, "2026-11", _clock.UtcNow.AddDays(5));
            Assert.True(inv.IsSuccess);
            invoiceId = inv.Value.Id;
        }

        _clock.UtcNow = _clock.UtcNow.AddDays(15); // 10 dias após vencimento
        using (var delinquency = CreateScope())
        {
            var summary = await delinquency.Plans.ProcessDelinquencyAsync(_masterUser, gracePeriodDaysBeforeSuspension: 5);
            Assert.True(summary.IsSuccess);
            Assert.Equal(1, summary.Value.NewlyOverdueInvoices);
            Assert.Equal(1, summary.Value.SuspendedSubscriptions);

            // Cliente suspenso não pode operar
            var blocked = await delinquency.Enforcement.CheckModuleAccessAsync(customerId, SystemModules.Pdv);
            Assert.True(blocked.IsFailure);
            Assert.Equal("Subscription.SuspendedOrExpired", blocked.Error.Code);

            // Pagamento quita e reativa
            var paid = await delinquency.Plans.RegisterInvoicePaymentAsync(_masterUser, subscriptionId, invoiceId, "pix-987", "PIX");
            Assert.True(paid.IsSuccess);

            var restored = await delinquency.Enforcement.CheckModuleAccessAsync(customerId, SystemModules.Pdv);
            Assert.True(restored.IsSuccess);
        }

        // 4. Suporte, configurações globais e indicadores do Painel Master
        using (var dashboard = CreateScope())
        {
            await dashboard.Ops.UpsertGlobalSettingAsync(_masterUser, "delinquency.grace_days", "5", "Dias de tolerância");
            await dashboard.Ops.OpenSupportTicketAsync(_masterUser, customerId, null, "Dúvida fiscal", "Configurar impressora");

            var indicators = await dashboard.Ops.GetOverviewIndicatorsAsync(_masterUser);
            Assert.True(indicators.IsSuccess);
            Assert.Equal(1, indicators.Value.TotalCustomers);
            Assert.Equal(1, indicators.Value.ActiveSubscriptions);
            Assert.Equal(699.90m, indicators.Value.MonthlyRecurringRevenueEstimate);
            Assert.Equal(699.90m, indicators.Value.PaidRevenueTotal);
            Assert.Equal(1, indicators.Value.OpenSupportTicketsCount);
        }
    }
}
