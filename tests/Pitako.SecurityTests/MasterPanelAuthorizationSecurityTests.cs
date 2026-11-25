using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.MasterPanel.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class MasterPanelAuthorizationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

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

    [Fact]
    public async Task EstablishmentUser_ShouldBeBlockedFromAllMasterPanelOperations()
    {
        var storeAdmin = User.CreateEstablishmentUser(
            Guid.NewGuid(), "Admin Loja", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var saasSupport = User.CreateSaasUser(
            "Suporte SaaS", "suporte@pitako.com", "hash", SaasRole.Support).Value;

        await using var db = CreateDb();
        var planRepo = new EfCommercialPlanRepository(db);
        var customerRepo = new EfMasterCustomerRepository(db);
        var subRepo = new EfSubscriptionRepository(db);
        var ticketRepo = new EfSupportTicketRepository(db);
        var settingRepo = new EfGlobalSettingRepository(db);

        var planSvc = new MasterPlanAndSubscriptionService(planRepo, customerRepo, subRepo, _clock);
        var opsSvc = new MasterOperationsAndDashboardService(customerRepo, subRepo, planRepo, ticketRepo, settingRepo, _clock);

        var createCmd = new CreatePlanCommand(
            "hack", "Hack Plan", "Tentativa indevida", 0m, "BRL", BillingCycle.Monthly, 0,
            new PlanQuotaConfiguration(
                QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(),
                QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited()),
            [SystemModules.Pdv]);

        // Admin de loja não cria plano, não altera setting global e não vê indicadores Master
        var createByStore = await planSvc.CreatePlanAsync(storeAdmin, createCmd);
        Assert.True(createByStore.IsFailure);
        Assert.Equal("MasterPanel.EstablishmentForbidden", createByStore.Error.Code);

        var settingByStore = await opsSvc.UpsertGlobalSettingAsync(storeAdmin, "key", "val");
        Assert.True(settingByStore.IsFailure);
        Assert.Equal("MasterPanel.EstablishmentForbidden", settingByStore.Error.Code);

        var dashboardByStore = await opsSvc.GetOverviewIndicatorsAsync(storeAdmin);
        Assert.True(dashboardByStore.IsFailure);
        Assert.Equal("MasterPanel.EstablishmentForbidden", dashboardByStore.Error.Code);

        // Perfil Suporte SaaS (menor privilégio: apenas View/Edit) não cria plano nem administra settings globais
        var createBySupport = await planSvc.CreatePlanAsync(saasSupport, createCmd);
        Assert.True(createBySupport.IsFailure);
        Assert.Equal("MasterPanel.InsufficientPermission", createBySupport.Error.Code);

        var settingBySupport = await opsSvc.UpsertGlobalSettingAsync(saasSupport, "key", "val");
        Assert.True(settingBySupport.IsFailure);
        Assert.Equal("MasterPanel.InsufficientPermission", settingBySupport.Error.Code);
    }
}
