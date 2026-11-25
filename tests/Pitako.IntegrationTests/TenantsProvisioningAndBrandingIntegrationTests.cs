using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.MasterPanel.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class TenantsProvisioningAndBrandingIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _masterConn = new("Data Source=:memory:");
    private readonly SqliteConnection _tenantsConn = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 24, 22, 30, 0, TimeSpan.Zero));
    private readonly User _saasAdmin = User.CreateSaasUser("Master", "master@pitako.com", "hash", SaasRole.MasterAdministrator).Value;

    public async Task InitializeAsync()
    {
        await _masterConn.OpenAsync();
        await _tenantsConn.OpenAsync();

        await using (var mdb = CreateMasterDb())
            await mdb.Database.EnsureCreatedAsync();

        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.BackgroundJob);
        await using (var tdb = CreateTenantsDb(ctx))
            await tdb.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _masterConn.DisposeAsync();
        await _tenantsConn.DisposeAsync();
    }

    private MasterPanelDbContext CreateMasterDb() =>
        new(new DbContextOptionsBuilder<MasterPanelDbContext>().UseSqlite(_masterConn).Options, _clock);

    private TenantsDbContext CreateTenantsDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<TenantsDbContext>().UseSqlite(_tenantsConn).Options, ctx, _clock);

    [Fact]
    public async Task Provisioning_ShouldEnforcePlanLimits_AndAllowUnlimitedScaling_PlusIndependentBrandingPreviewPublish()
    {
        Guid basicPlanId;
        Guid unlimitedPlanId;
        Guid customerId;
        Guid subscriptionId;

        await using (var mdb = CreateMasterDb())
        {
            var planSvc = new MasterPlanAndSubscriptionService(
                new EfCommercialPlanRepository(mdb),
                new EfMasterCustomerRepository(mdb),
                new EfSubscriptionRepository(mdb),
                _clock);

            basicPlanId = (await planSvc.CreatePlanAsync(_saasAdmin, new CreatePlanCommand(
                "basico", "Básico", "1 loja", 149m, "BRL", BillingCycle.Monthly, 0,
                new PlanQuotaConfiguration(
                    QuotaLimit.Limited(1).Value, QuotaLimit.Limited(1).Value, QuotaLimit.Limited(1).Value,
                    QuotaLimit.Limited(5).Value, QuotaLimit.Limited(2).Value, QuotaLimit.Limited(2).Value, QuotaLimit.Unlimited()),
                [SystemModules.Tenants, SystemModules.Catalog, SystemModules.Acai]))).Value.Id;

            unlimitedPlanId = (await planSvc.CreatePlanAsync(_saasAdmin, new CreatePlanCommand(
                "ilimitado", "Ilimitado", "Sem limite de lojas", 799m, "BRL", BillingCycle.Monthly, 0,
                new PlanQuotaConfiguration(
                    QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(),
                    QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited(), QuotaLimit.Unlimited()),
                [SystemModules.Tenants, SystemModules.Catalog, SystemModules.Acai]))).Value.Id;

            var subCreated = (await planSvc.CreateCustomerSubscriptionAsync(
                _saasAdmin, "Grupo Roxo", "11222333000181", "contato@gruporoxo.com", null, basicPlanId, startWithTrial: false)).Value;
            customerId = subCreated.Customer.Id;
            subscriptionId = subCreated.Subscription.Id;
        }

        Guid store1Id;
        await using (var mdb = CreateMasterDb())
        await using (var tdb = CreateTenantsDb(new TenantContext()))
        {
            var tenantCtx = new TenantContext();
            await using var tdbScoped = CreateTenantsDb(tenantCtx);

            var enforcement = new PlanEntitlementEnforcementService(
                new EfSubscriptionRepository(mdb), new EfCommercialPlanRepository(mdb), _clock);
            var provisioning = new TenantProvisioningService(
                new EfTenantRepository(tdbScoped),
                new EfStoreUnitRepository(tdbScoped),
                new EfStoreVisualIdentityRepository(tdbScoped),
                new MasterPlanQuotaPolicyChecker(enforcement),
                tenantCtx,
                tenantCtx);

            // 1. Primeira loja (Açaíteria) criada dentro da cota do Básico
            var res1 = await provisioning.CreateEstablishmentAsync(_saasAdmin, new CreateEstablishmentCommand(
                customerId, "Açaí Roxo LTDA", "Açaí Roxo", "acai-roxo", "11.222.333/0001-81",
                BusinessSegmentCatalog.Acaiteria, "Av. Central, 100", "11988887777", "loja@acairoxo.com"));
            Assert.True(res1.IsSuccess);
            Assert.True(res1.Value.SegmentProfile.SupportsAcaiBuilder);
            store1Id = res1.Value.Tenant.Id;

            // 2. Segunda loja bloqueada no Básico no backend
            var res2Blocked = await provisioning.CreateEstablishmentAsync(_saasAdmin, new CreateEstablishmentCommand(
                customerId, "Pizzaria Roxo LTDA", "Pizzaria Roxo", "pizzaria-roxo", "06.990.590/0001-23",
                BusinessSegmentCatalog.Pizzaria));
            Assert.True(res2Blocked.IsFailure);
            Assert.Equal("Plan.LimitExceeded", res2Blocked.Error.Code);
        }

        // 3. Upgrade para Ilimitado permite criar 2ª loja e filial extra
        await using (var mdb = CreateMasterDb())
        {
            var planSvc = new MasterPlanAndSubscriptionService(
                new EfCommercialPlanRepository(mdb),
                new EfMasterCustomerRepository(mdb),
                new EfSubscriptionRepository(mdb),
                _clock);

            var upgrade = await planSvc.ChangeSubscriptionPlanAsync(
                _saasAdmin, subscriptionId, unlimitedPlanId, new CurrentUsageSnapshot(1, 1, 0, 1, 1, 1));
            Assert.True(upgrade.IsSuccess);

            var tenantCtx = new TenantContext();
            await using var tdbScoped = CreateTenantsDb(tenantCtx);
            var enforcement = new PlanEntitlementEnforcementService(
                new EfSubscriptionRepository(mdb), new EfCommercialPlanRepository(mdb), _clock);
            var provisioning = new TenantProvisioningService(
                new EfTenantRepository(tdbScoped),
                new EfStoreUnitRepository(tdbScoped),
                new EfStoreVisualIdentityRepository(tdbScoped),
                new MasterPlanQuotaPolicyChecker(enforcement),
                tenantCtx,
                tenantCtx);

            var res2Ok = await provisioning.CreateEstablishmentAsync(_saasAdmin, new CreateEstablishmentCommand(
                customerId, "Pizzaria Roxo LTDA", "Pizzaria Roxo", "pizzaria-roxo", "06.990.590/0001-23",
                BusinessSegmentCatalog.Pizzaria));
            Assert.True(res2Ok.IsSuccess);
            Assert.True(res2Ok.Value.SegmentProfile.SupportsFractionalItems);
        }

        // 4. Personalização independente da Loja 1: rascunho -> preview -> publicação
        var storeAdmin = User.CreateEstablishmentUser(store1Id, "Dono Roxo", "dono@acairoxo.com", "hash", EstablishmentRole.Administrator).Value;
        var ctxStore1 = new TenantContext();
        ctxStore1.SetTenant(store1Id, TenantResolutionSource.AuthenticatedSession);

        await using (var tdb = CreateTenantsDb(ctxStore1))
        {
            var brandingSvc = new StoreBrandingService(
                new EfTenantRepository(tdb),
                new EfStoreVisualIdentityRepository(tdb),
                ctxStore1,
                _clock);

            var draft = await brandingSvc.UpdateDraftAsync(storeAdmin, store1Id, new UpdateStoreBrandingDraftCommand(
                "https://cdn.acairoxo.com/logo.svg",
                "https://cdn.acairoxo.com/capa.webp",
                "Energia roxa todo dia",
                "#6D28D9",
                "#FFFFFF",
                "#10B981",
                "showcase",
                [new StoreBannerSnapshot("Copo 500ml", "https://cdn.acairoxo.com/b1.jpg", null, 1)]));
            Assert.True(draft.IsSuccess);

            // Antes de publicar, storefront pública ainda não exibe logo/cores do rascunho
            var beforePublish = await brandingSvc.GetPublicStorefrontBySlugAsync("acai-roxo");
            Assert.True(beforePublish.IsSuccess);
            Assert.Null(beforePublish.Value.Branding.LogoUrl);

            // Preview interno exibe rascunho
            var preview = await brandingSvc.GetDraftPreviewAsync(storeAdmin, store1Id);
            Assert.Equal("https://cdn.acairoxo.com/logo.svg", preview.Value.LogoUrl);

            // Publica e verifica storefront
            var published = await brandingSvc.PublishBrandingAsync(storeAdmin, store1Id);
            Assert.True(published.IsSuccess);

            var afterPublish = await brandingSvc.GetPublicStorefrontBySlugAsync("acai-roxo");
            Assert.True(afterPublish.IsSuccess);
            Assert.Equal("https://cdn.acairoxo.com/logo.svg", afterPublish.Value.Branding.LogoUrl);
            Assert.Equal("#6D28D9", afterPublish.Value.Branding.PrimaryColorHex);
            Assert.Equal("11.222.333/0001-81", afterPublish.Value.FormattedCnpj);
            Assert.Single(afterPublish.Value.Branding.Banners);
        }
    }
}
