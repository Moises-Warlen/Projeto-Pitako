using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Finance.Application.Services;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Bootstrap;

/// <summary>
/// Semente idempotente exclusivamente para ambiente de desenvolvimento/demonstração local.
/// Não utiliza marca PITAKO nos dados do estabelecimento demo (identidade independente da loja).
/// </summary>
public static class PitakoDevDemoSeeder
{
    public static async Task BootstrapProductionMasterIfConfiguredAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken ct = default)
    {
        var email = configuration["BOOTSTRAP_MASTER_EMAIL"] ?? Environment.GetEnvironmentVariable("BOOTSTRAP_MASTER_EMAIL");
        var password = configuration["BOOTSTRAP_MASTER_PASSWORD"] ?? Environment.GetEnvironmentVariable("BOOTSTRAP_MASTER_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContextSetter>().SetMasterScope();
        var userRepo = sp.GetRequiredService<IUserRepository>();
        var existing = await userRepo.FindByEmailAsync(UserScope.Saas, null, User.NormalizeEmail(email), ct);
        if (existing is not null)
            return;

        var userAuth = sp.GetRequiredService<UserAuthorizationService>();
        await userAuth.CreateSaasUserAsync(
            null,
            "Administrador Master PITAKO",
            email.Trim(),
            password,
            SaasRole.MasterAdministrator,
            "127.0.0.1",
            ct);
    }

    public static async Task SeedIfNeededAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var setter = sp.GetRequiredService<ITenantContextSetter>();
        var userRepo = sp.GetRequiredService<IUserRepository>();
        var userAuth = sp.GetRequiredService<UserAuthorizationService>();
        var planService = sp.GetRequiredService<MasterPlanAndSubscriptionService>();
        var tenantProv = sp.GetRequiredService<TenantProvisioningService>();
        var brandingService = sp.GetRequiredService<StoreBrandingService>();
        var catalogService = sp.GetRequiredService<CatalogManagementService>();
        var acaiService = sp.GetRequiredService<AcaiBuilderManagementService>();
        var diningService = sp.GetRequiredService<DiningHallManagementService>();
        var totemService = sp.GetRequiredService<TotemSelfServiceService>();
        var deliveryPolicyService = sp.GetRequiredService<DeliveryPolicyAndFreightService>();
        var courierService = sp.GetRequiredService<CourierDispatchAndGpsService>();
        var financeService = sp.GetRequiredService<FinanceManagementService>();
        var clock = sp.GetRequiredService<IClock>();

        setter.SetMasterScope();

        var existingMaster = await userRepo.FindByEmailAsync(UserScope.Saas, null, "MASTER@PITAKO.COM.BR", ct);
        if (existingMaster is not null)
            return;

        // 1. Admin Master SaaS
        var masterRes = await userAuth.CreateSaasUserAsync(
            null,
            "Administrador Master PITAKO",
            "master@pitako.com.br",
            "Master@12345",
            SaasRole.MasterAdministrator,
            "127.0.0.1",
            ct);
        if (masterRes.IsFailure)
            return;
        var master = masterRes.Value;

        // 2. Planos Comerciais (incluindo Plano Ilimitado sem teto artificial)
        var unlimitedQuotas = new PlanQuotaConfiguration(
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited());

        var planUnlimitedRes = await planService.CreatePlanAsync(
            master,
            new CreatePlanCommand(
                "ilimitado",
                "Plano Ilimitado",
                "Todos os módulos habilitados sem limites artificiais.",
                499.90m,
                "BRL",
                BillingCycle.Monthly,
                14,
                unlimitedQuotas,
                SystemModules.EstablishmentModules,
                ["white_label", "api_access", "offline_sync"]),
            ct);
        if (planUnlimitedRes.IsFailure)
            return;

        var proQuotas = new PlanQuotaConfiguration(
            QuotaLimit.Limited(1).Value,
            QuotaLimit.Limited(3).Value,
            QuotaLimit.Limited(4).Value,
            QuotaLimit.Limited(15).Value,
            QuotaLimit.Limited(10).Value,
            QuotaLimit.Limited(10).Value,
            QuotaLimit.Limited(5000).Value);

        await planService.CreatePlanAsync(
            master,
            new CreatePlanCommand(
                "pro",
                "Plano Profissional",
                "Plano completo para operação multi-canal.",
                249.90m,
                "BRL",
                BillingCycle.Monthly,
                14,
                proQuotas,
                [
                    SystemModules.Tenants, SystemModules.Catalog, SystemModules.Acai,
                    SystemModules.Orders, SystemModules.Pdv, SystemModules.DiningHall,
                    SystemModules.Delivery, SystemModules.Printing, SystemModules.Reports,
                    SystemModules.Finance, SystemModules.Security
                ]),
            ct);

        // 3. Cliente SaaS e Estabelecimento Demo (marca exclusiva da loja, sem marca PITAKO)
        var subRes = await planService.CreateCustomerSubscriptionAsync(
            master,
            "Tropical Foods Comércio Ltda",
            "11222333000181",
            "contato@tropicalexpress.com.br",
            "11999998888",
            planUnlimitedRes.Value.Id,
            startWithTrial: false,
            cancellationToken: ct);
        if (subRes.IsFailure)
            return;

        var tenantRes = await tenantProv.CreateEstablishmentAsync(
            master,
            new CreateEstablishmentCommand(
                subRes.Value.Customer.Id,
                "Tropical Foods Comércio Ltda",
                "Açaí Tropical Express",
                "acai-demo",
                "11222333000181",
                BusinessSegmentCatalog.Acaiteria,
                "Av. Atlântica, 450 — Centro",
                "11999998888",
                "contato@tropicalexpress.com.br"),
            ct);
        if (tenantRes.IsFailure)
            return;

        var tenant = tenantRes.Value.Tenant;

        // TenantProvisioningService já vinculou _tenantContextSetter ao novo tenant!
        var adminRes = await userAuth.CreateEstablishmentUserAsync(
            master,
            tenant.Id,
            "Gerente Tropical",
            "admin@tropicalexpress.com.br",
            "Store@12345",
            EstablishmentRole.Administrator,
            "127.0.0.1",
            ct);
        if (adminRes.IsFailure)
            return;
        var storeAdmin = adminRes.Value;

        await userAuth.CreateEstablishmentUserAsync(
            storeAdmin,
            tenant.Id,
            "Operador Caixa 01",
            "caixa@tropicalexpress.com.br",
            "Caixa@12345",
            EstablishmentRole.Cashier,
            "127.0.0.1",
            ct);

        // Identidade visual independente da loja (roxo açaí + creme + âmbar, sem vermelho PITAKO)
        await brandingService.UpdateDraftAsync(
            storeAdmin,
            tenant.Id,
            new UpdateStoreBrandingDraftCommand(
                "/store-assets/tropical-logo.svg",
                "/store-assets/tropical-cover.svg",
                "Açaí artesanal batido na hora com frutas frescas",
                "#4C1D95",
                "#FAF5FF",
                "#F59E0B",
                "grid"),
            ct);
        await brandingService.PublishBrandingAsync(storeAdmin, tenant.Id, ct);

        // 4. Catálogo & Montador de Açaí
        var catAcai = (await catalogService.CreateCategoryAsync(storeAdmin, "Tigelas & Copos", "Monte do seu jeito", null, 1, ct)).Value;
        var catBebidas = (await catalogService.CreateCategoryAsync(storeAdmin, "Sucos & Vitaminas", "Bebidas naturais", null, 2, ct)).Value;

        await catalogService.CreateProductAsync(storeAdmin, catAcai.Id, "Açaí Tradicional 500ml Pronto", 22.90m, "Batido puro com banana", "ACAI-500", ProductSaleMode.Unit, true, 1, ct);
        await catalogService.CreateProductAsync(storeAdmin, catAcai.Id, "Açaí Self-Service por Quilo", 64.90m, "Pesagem na balança do balcão", "ACAI-KG", ProductSaleMode.Weight, false, 2, ct);
        await catalogService.CreateProductAsync(storeAdmin, catBebidas.Id, "Vitamina de Açaí c/ Guaraná 400ml", 16.50m, "Refrescante", "VIT-400", ProductSaleMode.Unit, false, 1, ct);

        await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 300ml", 300, 15.90m, 3, 0, AcaiFreeCountingMode.ByPortion, 5, null, "CUP-300", 1, ct);
        await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 500ml", 500, 21.90m, 4, 0, AcaiFreeCountingMode.ByPortion, 6, null, "CUP-500", 2, ct);
        await acaiService.CreateBaseFlavorAsync(storeAdmin, "Açaí Puro Tradicional", 0m, 1, ct);
        await acaiService.CreateBaseFlavorAsync(storeAdmin, "Creme de Cupuaçu", 2.00m, 2, ct);
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Granola Crocante", 2, 1, cancellationToken: ct);
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Leite em Pó", 2, 2, cancellationToken: ct);
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Banana Fatiada", 2, 3, cancellationToken: ct);
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Creme de Avelã (Nutella)", 5.50m, 2, 1, cancellationToken: ct);
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Morango Fresco", 4.00m, 2, 2, cancellationToken: ct);

        // 5. Salão & Mesas
        var sector = (await diningService.CreateSectorAsync(storeAdmin, "Salão Principal", null, 1, ct)).Value;
        await diningService.CreateTableAsync(storeAdmin, sector.Id, "M01", 4, "qr-acai-demo-m01", ct);
        await diningService.CreateTableAsync(storeAdmin, sector.Id, "M02", 4, "qr-acai-demo-m02", ct);

        // 6. Financeiro inicial
        var entry = (await financeService.CreateEntryAsync(storeAdmin, FinancialEntryKind.Receivable, DreLineCategory.GrossSalesRevenue, "Vendas Balcão", "Fechamento inicial", 1250.00m, clock.UtcNow, "PDV-001", null, ct)).Value;
        await financeService.SettleEntryAsync(storeAdmin, entry.Id, 1215.00m, 35.00m, "PIX / Débito", cancellationToken: ct);
        await financeService.CreateEntryAsync(storeAdmin, FinancialEntryKind.Payable, DreLineCategory.CogsSuppliesAndInputs, "Distribuidora Amazônia", "Polpa de Açaí 40kg", 480.00m, clock.UtcNow.AddDays(3), "NF-9081", null, ct);

        // 7. Totem de Autoatendimento, Política de Frete & Entregadores
        await totemService.RegisterTerminalAsync(storeAdmin, "TOTEM-01", "Totem Salão Principal", TotemFulfillmentChoice.CounterPickup, false, false, 120, null, ct);
        await deliveryPolicyService.CreatePolicyAsync(storeAdmin, -20.5386, -47.4008, 12m, FreightCalculationMode.FixedFlat, 6.00m, 3m, 2.00m, 35, 15.00m, 60.00m, null, true, true, ct);
        await courierService.RegisterCourierAsync(storeAdmin, "João Silva (Motoboy 01)", "16999112233", CourierVehicleType.Motorcycle, CourierCommissionMode.FixedPerDelivery, 6.00m, 0m, null, "ABC-1234", ct);
    }
}
