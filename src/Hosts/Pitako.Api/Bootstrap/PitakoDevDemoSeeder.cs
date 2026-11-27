using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Finance.Application.Services;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.Printing.Application.Services;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
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
        var marketplaceHubService = sp.GetRequiredService<MarketplaceHubService>();
        var printingService = sp.GetRequiredService<PrintingManagementService>();
        var couponsService = sp.GetRequiredService<Pitako.Modules.Coupons.Application.Services.CouponsManagementService>();
        var waService = sp.GetRequiredService<WhatsAppSupportService>();
        var clock = sp.GetRequiredService<IClock>();

        setter.SetMasterScope();

        var existingMaster = await userRepo.FindByEmailAsync(UserScope.Saas, null, "MASTER@PITAKO.COM.BR", ct);
        if (existingMaster is not null)
        {
            await UpgradeLegacyAcaiDemoIfNeededAsync(sp, ct);
            return;
        }

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
                SystemModules.EstablishmentModules),
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
        var defaultHours = System.Text.Json.JsonSerializer.Serialize(
            Enumerable.Range(0, 7).Select(d => new StoreOperatingHoursEntry(d, true, "08:00", "23:00")));

        await brandingService.UpdateDraftAsync(
            storeAdmin,
            tenant.Id,
            new UpdateStoreBrandingDraftCommand(
                "/store-assets/tropical-logo.svg",
                "/store-assets/tropical-cover.svg",
                "Mais que açaí, é puro sabor! Monte do seu jeito e receba super gelado.",
                "#4C1D95",
                "#FAF5FF",
                "#F59E0B",
                "showcase",
                [
                    new StoreBannerSnapshot("Linha Explosão Premium", "/store-assets/acai/pistache.jpg", "#explosao-premium", 1),
                    new StoreBannerSnapshot("Monte do Seu Jeito 300ml a 1L", "/store-assets/acai/monte-seu-jeito.jpg", "#monte-seu-acai", 2),
                    new StoreBannerSnapshot("Açaí na Garrafa 300ml", "/store-assets/acai/garrafa-morango.jpg", "#acai-na-garrafa", 3)
                ],
                defaultHours,
                "Açaí cremoso, frutas frescas selecionadas e entrega rápida!",
                "16991098863",
                "@acaitropicalexpress"),
            ct);
        await brandingService.PublishBrandingAsync(storeAdmin, tenant.Id, ct);

        // 4. Catálogo & Montador de Açaí
        await sp.GetRequiredService<SegmentCatalogTemplateService>().ApplyTemplateAsync(storeAdmin, "acaiteria", ct);
        var starterProv = sp.GetRequiredService<StoreStarterProvisioner>();
        await starterProv.ImportSpreadsheetKitAsync(storeAdmin, "acaiteria", ct);

        var demoCatRepo = sp.GetRequiredService<ICategoryRepository>();
        var catFamilia = (await demoCatRepo.ListOrderedAsync(ct))
            .FirstOrDefault(c => c.Name.Equals("Explosão Família (Potes)", StringComparison.OrdinalIgnoreCase));
        if (catFamilia is not null)
        {
            var pKg = (await catalogService.CreateProductAsync(storeAdmin, catFamilia.Id, "Açaí Self-Service por Quilo", 64.90m, "Pesagem na balança do balcão (preço por kg).", "ACAI-KG", ProductSaleMode.Weight, false, 99, ct)).Value;
            await catalogService.AddProductImageAsync(storeAdmin, pKg.Id, "/store-assets/acai/puro-100.jpg", 0, true, ct);
        }

        var seededProds = await sp.GetRequiredService<IProductRepository>().ListAllOrderedAsync(ct);
        var promoTargets = seededProds
            .Where(p => p.Name.Equals("Clássico", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Mousse de Morango", StringComparison.OrdinalIgnoreCase))
            .Select(p => (PromotionTargetType.Product, p.Id))
            .ToList();
        if (promoTargets.Count > 0)
        {
            await catalogService.CreatePromotionAsync(storeAdmin, "Ofertas Especiais Açaí", PromotionDiscountType.FixedAmount, 2.00m, clock.UtcNow.AddDays(-1), clock.UtcNow.AddYears(2), promoTargets, true, ct);
        }

        await SeedAcaiBuilderDefaultsAsync(acaiService, storeAdmin, ct);

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

        // 8. Conexões iniciais de Marketplace da loja demo (iFood & 99Food)
        var ifoodAcc = await marketplaceHubService.ConnectPartnerAccountAsync(
            storeAdmin, MarketplaceProvider.IFood, "iFood — Açaí Tropical Express", "ifood-client-acai-demo", "ifood-hmac-demo-secret-key", ct);
        if (ifoodAcc.IsSuccess)
        {
            await marketplaceHubService.BindExternalStoreAsync(
                storeAdmin, ifoodAcc.Value.Id, "IFOOD-TROPICAL-01", "Açaí Tropical Express (iFood)", null, true, ct);
        }

        var food99Acc = await marketplaceHubService.ConnectPartnerAccountAsync(
            storeAdmin, MarketplaceProvider.Food99, "99Food — Açaí Tropical Express", "99food-app-acai-demo", "99food-hmac-demo-secret-key", ct);
        if (food99Acc.IsSuccess)
        {
            await marketplaceHubService.BindExternalStoreAsync(
                storeAdmin, food99Acc.Value.Id, "99FOOD-TROPICAL-01", "Açaí Tropical Express (99Food)", null, false, ct);
        }

        // 9. Impressoras Térmicas, Cupom e Canal WhatsApp Bot + IA demo
        await printingService.RegisterPrinterAsync(storeAdmin, "COZINHA-01", "Impressora Cozinha / Produção (1ª Via)", PrinterConnectionKind.NetworkTcpEscPos, "192.168.0.100:9100", PaperWidthMm.Mm80, null, true, null, ct);
        await printingService.RegisterPrinterAsync(storeAdmin, "CAIXA-01", "Impressora Caixa / Balcão & Entrega (2ª Via)", PrinterConnectionKind.UsbSpooler, "USB001", PaperWidthMm.Mm80, null, true, null, ct);
        await couponsService.CreateCouponAsync(storeAdmin, "PRIMEIRACOMPRA", Pitako.Modules.Coupons.Domain.CouponDiscountType.FixedAmount, 5.00m, clock.UtcNow.AddDays(-1), clock.UtcNow.AddYears(2), 25.00m, null, 200, null, cancellationToken: ct);
        await waService.ConfigureChannelAsync(storeAdmin, tenantRes.Value.Tenant.TradeName, "5516991098863", "wa-acai-demo", "waba-acai-demo", "wa-cloud-demo", "wa-secret-demo", "verify-acai-demo", WhatsAppAutoReplyMode.HybridBotThenAi, "/loja/acai-demo", null, ct);
    }

    public static async Task SeedAcaiBuilderDefaultsAsync(
        AcaiBuilderManagementService acaiService,
        User storeAdmin,
        CancellationToken ct)
    {
        await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 300 ml", 300, 15.90m, 3, 0, AcaiFreeCountingMode.ByPortion, 6, null, "CUP-300", 1, ct, "200g de açaí • até 3 acompanhamentos grátis", 6.80m, true, true);
        await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 500 ml", 500, 19.90m, 4, 0, AcaiFreeCountingMode.ByPortion, 8, null, "CUP-500", 2, ct, "330g de açaí • até 4 acompanhamentos grátis", 9.40m, true, true);
        await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 700 ml", 700, 26.90m, 5, 0, AcaiFreeCountingMode.ByPortion, 10, null, "CUP-700", 3, ct, "460g de açaí • até 5 acompanhamentos grátis", 12.50m, true, true);

        await acaiService.CreateBaseFlavorAsync(storeAdmin, "Açaí Tradicional Cremoso", 0m, 1, ct);
        await acaiService.CreateBaseFlavorAsync(storeAdmin, "Açaí Zero Açúcar", 3.00m, 2, ct);

        await acaiService.CreateFreeToppingAsync(storeAdmin, "Leite Ninho em pó", 2, 1, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 0.80m, categoryName: "Acompanhamentos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Leite condensado", 2, 2, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 0.70m, categoryName: "Acompanhamentos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Granola tradicional", 2, 3, cancellationToken: ct, description: "Porção 30g", costPriceAmount: 1.10m, categoryName: "Acompanhamentos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Paçoca", 2, 4, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 0.68m, categoryName: "Acompanhamentos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Amendoim triturado", 2, 5, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 1.00m, categoryName: "Acompanhamentos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Chocobol", 2, 6, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 1.05m, categoryName: "Confeitos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Confete", 2, 7, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 1.10m, categoryName: "Confeitos");
        await acaiService.CreateFreeToppingAsync(storeAdmin, "Ovomaltine", 2, 8, cancellationToken: ct, description: "Porção 25g", costPriceAmount: 1.33m, categoryName: "Complementos");

        await acaiService.CreatePaidAddonAsync(storeAdmin, "Morango Fresco", 4.50m, 2, 1, cancellationToken: ct, description: "Porção 45g", costPriceAmount: 1.36m, categoryName: "Frutas");
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Banana Fatiada", 3.90m, 2, 2, cancellationToken: ct, description: "Porção 40g", costPriceAmount: 0.32m, categoryName: "Frutas");
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Kiwi Fatiado", 4.00m, 2, 3, cancellationToken: ct, description: "Porção 40g", costPriceAmount: 1.40m, categoryName: "Frutas");
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Nutella Original", 6.50m, 2, 4, cancellationToken: ct, description: "Porção 30g", costPriceAmount: 2.77m, categoryName: "Adicionais");
        await acaiService.CreatePaidAddonAsync(storeAdmin, "Creme Gourmet de Pistache", 6.00m, 2, 5, cancellationToken: ct, description: "Porção 30g", costPriceAmount: 2.25m, categoryName: "Adicionais");
    }

    private static async Task UpgradeLegacyAcaiDemoIfNeededAsync(IServiceProvider sp, CancellationToken ct)
    {
        var tenantRepo = sp.GetRequiredService<ITenantRepository>();
        var allTenants = await tenantRepo.ListAllAsync(ct);
        var acaiTenants = allTenants
            .Where(t => StoreStarterProvisioner.NormalizeSegmentKey(t.SegmentCode) == "acaiteria")
            .ToList();
        if (acaiTenants.Count == 0) return;

        foreach (var tenant in acaiTenants)
        {
            await UpgradeSingleAcaiTenantIfNeededAsync(sp, tenant, ct);
        }
    }

    private static async Task UpgradeSingleAcaiTenantIfNeededAsync(IServiceProvider sp, Tenant tenant, CancellationToken ct)
    {
        var setter = sp.GetRequiredService<ITenantContextSetter>();
        setter.SetTenant(tenant.Id, TenantResolutionSource.BackgroundJob);

        var catRepo = sp.GetRequiredService<ICategoryRepository>();
        var existingCats = await catRepo.ListOrderedAsync(ct);
        var storeAdmin = User.CreateEstablishmentUser(
            tenant.Id,
            $"Gestor {tenant.TradeName}",
            tenant.ContactEmail ?? $"admin@{tenant.Slug}.local",
            "AutoProvision!2026",
            EstablishmentRole.Administrator).Value;

        var catalogService = sp.GetRequiredService<CatalogManagementService>();
        var prodRepo = sp.GetRequiredService<IProductRepository>();
        var modRepo = sp.GetRequiredService<IModifierGroupRepository>();

        var hasNewCatalog = existingCats.Any(c => c.Name.Equals("Explosão Clássica", StringComparison.OrdinalIgnoreCase));
        if (!hasNewCatalog)
        {
            var legacyStubNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Açaí Tradicional 500ml Pronto",
                "Explosão Ninho com Morango",
                "Açaí Clássico 500ml",
                "Pistache / Nutella Supremo 300ml",
                "Vitamina de Açaí c/ Guaraná 400ml",
                "Monte do Seu Jeito"
            };

            foreach (var p in await prodRepo.ListAllOrderedAsync(ct))
            {
                if (legacyStubNames.Contains(p.Name))
                {
                    await catalogService.DeleteOrArchiveProductAsync(storeAdmin, p.Id, ct);
                }
            }

            foreach (var g in await modRepo.ListAllAsync(ct))
            {
                await catalogService.DeleteModifierGroupAsync(storeAdmin, g.Id, ct);
            }

            await sp.GetRequiredService<SegmentCatalogTemplateService>().ApplyTemplateAsync(storeAdmin, "acaiteria", ct);
        }

        // Move "Açaí Self-Service por Quilo" (se legado) para "Explosão Família (Potes)" com foto real e limpa categorias vazias legadas/duplicadas
        var currentCats = await catRepo.ListOrderedAsync(ct);
        var catFamilia = currentCats.FirstOrDefault(c => c.Name.Equals("Explosão Família (Potes)", StringComparison.OrdinalIgnoreCase));
        var currentProds = await prodRepo.ListAllOrderedAsync(ct);
        var selfServiceProd = currentProds.FirstOrDefault(p => p.Name.Equals("Açaí Self-Service por Quilo", StringComparison.OrdinalIgnoreCase));
        if (selfServiceProd is not null && catFamilia is not null)
        {
            if (selfServiceProd.CategoryId != catFamilia.Id)
            {
                await catalogService.MoveProductToCategoryAsync(storeAdmin, selfServiceProd.Id, catFamilia.Id, 99, ct);
            }
            var refreshedKg = await prodRepo.FindByIdAsync(selfServiceProd.Id, ct);
            if (refreshedKg is not null && refreshedKg.Images.All(i => i.ImageUrl.Contains("/segment-art/", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var oldImg in refreshedKg.Images.ToList())
                {
                    await catalogService.RemoveProductImageAsync(storeAdmin, refreshedKg.Id, oldImg.Id, ct);
                }
                await catalogService.AddProductImageAsync(storeAdmin, refreshedKg.Id, "/store-assets/acai/puro-100.jpg", 0, true, ct);
            }
        }

        var legacyCatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tigelas & Copos",
            "Açaís Prontos da Casa",
            "Sucos & Vitaminas",
            "Mais Vendidos"
        };
        var refreshedProds = await prodRepo.ListAllOrderedAsync(ct);
        var refreshedCats = await catRepo.ListOrderedAsync(ct);
        foreach (var oldCat in refreshedCats.Where(c => legacyCatNames.Contains(c.Name)))
        {
            await catalogService.DeleteCategoryAsync(storeAdmin, oldCat.Id, ct, null);
        }
        // Remove categorias duplicadas vazias (ex: "Monte seu Açaí" extra sem produtos)
        foreach (var dupGroup in refreshedCats.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var extraCat in dupGroup)
            {
                if (!refreshedProds.Any(p => p.CategoryId == extraCat.Id && p.Status != CatalogItemStatus.Archived))
                {
                    await catalogService.DeleteCategoryAsync(storeAdmin, extraCat.Id, ct, null);
                }
            }
        }

        await sp.GetRequiredService<StoreStarterProvisioner>().ImportSpreadsheetKitAsync(storeAdmin, "acaiteria", ct);

        var brandingService = sp.GetRequiredService<StoreBrandingService>();
        var defaultAcaiBanners = new[]
        {
            new StoreBannerSnapshot("Linha Explosão Premium", "/store-assets/acai/pistache.jpg", "#explosao-premium", 1),
            new StoreBannerSnapshot("Monte do Seu Jeito 300ml a 1L", "/store-assets/acai/monte-seu-jeito.jpg", "#monte-seu-acai", 2),
            new StoreBannerSnapshot("Açaí na Garrafa 300ml", "/store-assets/acai/garrafa-morango.jpg", "#acai-na-garrafa", 3)
        };

        if (tenant.Slug.Equals("acai-demo", StringComparison.OrdinalIgnoreCase))
        {
            var hours = System.Text.Json.JsonSerializer.Serialize(
                Enumerable.Range(0, 7).Select(d => new StoreOperatingHoursEntry(d, true, "08:00", "23:00")));
            await brandingService.UpdateDraftAsync(
                storeAdmin,
                tenant.Id,
                new UpdateStoreBrandingDraftCommand(
                    "/store-assets/tropical-logo.svg",
                    "/store-assets/tropical-cover.svg",
                    "Mais que açaí, é puro sabor! Monte do seu jeito e receba super gelado.",
                    "#4C1D95",
                    "#FAF5FF",
                    "#F59E0B",
                    "showcase",
                    defaultAcaiBanners,
                    hours,
                    "Açaí cremoso, frutas frescas selecionadas e entrega rápida!",
                    "16991098863",
                    "@acaitropicalexpress"),
                ct);
            await brandingService.PublishBrandingAsync(storeAdmin, tenant.Id, ct);
        }
        else
        {
            var draftRes = await brandingService.GetDraftPreviewAsync(storeAdmin, tenant.Id, ct);
            if (draftRes.IsSuccess)
            {
                var cur = draftRes.Value;
                var curBanners = cur.Banners;
                if (!curBanners.Any() || !curBanners.Any(b => b.ImageUrl.Contains("/store-assets/acai/", StringComparison.OrdinalIgnoreCase)))
                {
                    await brandingService.UpdateDraftAsync(
                        storeAdmin,
                        tenant.Id,
                        new UpdateStoreBrandingDraftCommand(
                            cur.LogoUrl,
                            cur.CoverImageUrl,
                            cur.Slogan,
                            cur.PrimaryColorHex,
                            cur.SecondaryColorHex,
                            cur.AccentColorHex,
                            "showcase",
                            defaultAcaiBanners,
                            cur.OperatingHoursJson,
                            cur.AnnouncementText,
                            cur.WhatsAppNumber,
                            cur.InstagramHandle),
                        ct);
                    await brandingService.PublishBrandingAsync(storeAdmin, tenant.Id, ct);
                }
            }
        }

        var promoRepo = sp.GetRequiredService<IPromotionRepository>();
        if ((await promoRepo.ListActiveCandidatesAsync(ct)).Count == 0)
        {
            var upgradedProds = await prodRepo.ListAllOrderedAsync(ct);
            var promoTargets = upgradedProds
                .Where(p => p.Name.Equals("Clássico", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Mousse de Morango", StringComparison.OrdinalIgnoreCase))
                .Select(p => (PromotionTargetType.Product, p.Id))
                .ToList();
            if (promoTargets.Count > 0)
            {
                var utcNow = sp.GetRequiredService<IClock>().UtcNow;
                await catalogService.CreatePromotionAsync(storeAdmin, "Ofertas Especiais Açaí", PromotionDiscountType.FixedAmount, 2.00m, utcNow.AddDays(-1), utcNow.AddYears(2), promoTargets, true, ct);
            }
        }

        var cupRepo = sp.GetRequiredService<IAcaiCupSizeRepository>();
        var cups = await cupRepo.ListAsync(true, ct);
        var acaiService = sp.GetRequiredService<AcaiBuilderManagementService>();
        if (cups.Count == 0)
        {
            await SeedAcaiBuilderDefaultsAsync(acaiService, storeAdmin, ct);
        }
        else if (cups.All(c => c.VolumeMl != 700))
        {
            await acaiService.CreateCupSizeAsync(storeAdmin, "Copo 700 ml", 700, 26.90m, 5, 0, AcaiFreeCountingMode.ByPortion, 10, null, "CUP-700", 3, ct, "460g de açaí • até 5 acompanhamentos grátis", 12.50m, true, true);
        }
    }
}
