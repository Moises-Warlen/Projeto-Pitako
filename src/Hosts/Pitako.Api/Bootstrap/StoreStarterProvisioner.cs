using System.Text;
using System.Text.Json;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Application.Services;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Bootstrap;

/// <summary>
/// Auto-provisiona toda a estrutura operacional completa de qualquer nova loja criada
/// (identidade visual publicada com logo próprio, cardápio + insumos + fichas técnicas dos 9 modelos,
/// combos, salão/mesas QR, totem, frete/entregador, impressoras, cupons e canal WhatsApp Bot + IA).
/// </summary>
public sealed class StoreStarterProvisioner(
    ITenantContextSetter tenantSetter,
    StoreBrandingService brandingService,
    SegmentCatalogTemplateService segmentTemplateService,
    CatalogManagementService catalogService,
    ICategoryRepository categoryRepo,
    IProductRepository productRepo,
    IComboRepository comboRepo,
    IPromotionRepository promotionRepo,
    IAcaiCupSizeRepository acaiCupRepo,
    AcaiBuilderManagementService acaiService,
    ISupplyItemRepository supplyRepo,
    ITechnicalSheetRepository sheetRepo,
    SuppliesAndTechnicalSheetService inventoryService,
    IDiningSectorRepository sectorRepo,
    DiningHallManagementService diningService,
    ITotemTerminalRepository totemRepo,
    TotemSelfServiceService totemService,
    IDeliveryPolicyRepository deliveryPolicyRepo,
    DeliveryPolicyAndFreightService deliveryPolicyService,
    IDeliveryCourierRepository courierRepo,
    CourierDispatchAndGpsService courierService,
    IPrinterProfileRepository printerRepo,
    PrintingManagementService printingService,
    ICouponRepository couponRepo,
    CouponsManagementService couponsService,
    IWhatsAppChannelConfigRepository waRepo,
    WhatsAppSupportService waService,
    IClock clock)
{
    public sealed record KitImportSummary(int SuppliesCreated, int ProductsCreated, int SheetsCreated, int CombosCreated);

    private sealed record KitSupplyDef(string Key, string Name, decimal Price, decimal Qty, string Unit, string Category);
    private sealed record KitIngredientUse(string SupplyKey, decimal Qty, string Unit);
    private sealed record KitProductDef(string LineName, string Name, string SizeLabel, string Description, decimal Price, decimal PackagingCost, string Tags, bool Featured, KitIngredientUse[] Recipe);
    private sealed record KitComboDef(string Name, string Description, decimal DiscountPercent, string[] ProductNames);
    private sealed record KitPromoDef(string Name, string Description, decimal DiscountPercent, string[] ProductNames);

    private sealed record SegmentWorkbookKit(
        string PrimaryColor,
        string SecondaryColor,
        string AccentColor,
        string DefaultSlogan,
        KitSupplyDef[] Supplies,
        KitProductDef[] Products,
        KitComboDef[] Combos,
        KitPromoDef[] Promotions);

    public async Task ProvisionCompleteNewStoreAsync(Tenant tenant, CancellationToken ct = default)
    {
        tenantSetter.SetTenant(tenant.Id, TenantResolutionSource.BackgroundJob);

        var actor = User.CreateEstablishmentUser(
            tenant.Id,
            $"Gestor {tenant.TradeName}",
            tenant.ContactEmail ?? $"admin@{tenant.Slug}.local",
            "AutoProvision!2026",
            EstablishmentRole.Administrator).Value;

        var kit = ResolveKit(tenant.SegmentCode);
        var normalizedSeg = NormalizeSegmentKey(tenant.SegmentCode);
        var isAcai = normalizedSeg == "acaiteria";

        // 1. Visual Identity & Horários publicados com logotipo SVG monograma whitelabel (zero PITAKO)
        var monogramLogo = BuildDefaultMonogramLogoDataUrl(tenant.TradeName, kit.PrimaryColor, kit.AccentColor);
        var defaultHours = JsonSerializer.Serialize(
            Enumerable.Range(0, 7).Select(d => new StoreOperatingHoursEntry(d, true, "08:00", "22:00")));

        var defaultBanners = isAcai
            ? new[]
              {
                  new StoreBannerSnapshot("Linha Explosão Premium", "/store-assets/acai/pistache.jpg", "#explosao-premium", 1),
                  new StoreBannerSnapshot("Monte do Seu Jeito 300ml a 1L", "/store-assets/acai/monte-seu-jeito.jpg", "#monte-seu-acai", 2),
                  new StoreBannerSnapshot("Açaí na Garrafa 300ml", "/store-assets/acai/garrafa-morango.jpg", "#acai-na-garrafa", 3)
              }
            : [new StoreBannerSnapshot($"Promoção Especial — {tenant.TradeName}", monogramLogo, "#combos", 1)];

        await brandingService.UpdateDraftAsync(actor, tenant.Id, new UpdateStoreBrandingDraftCommand(
            monogramLogo,
            isAcai ? "/store-assets/tropical-cover.svg" : null,
            kit.DefaultSlogan,
            kit.PrimaryColor,
            kit.SecondaryColor,
            kit.AccentColor,
            "showcase",
            defaultBanners,
            defaultHours,
            isAcai ? "Açaí cremoso, frutas frescas selecionadas e entrega rápida!" : "Peça direto no nosso cardápio digital com descontos exclusivos.",
            tenant.ContactPhone ?? "16999000000",
            $"@{tenant.Slug}"), ct);

        await brandingService.PublishBrandingAsync(actor, tenant.Id, ct);

        // 2. Motor de Personalização + Planilha Ficha Técnica do Segmento (Insumos + Produtos + FTs + Combos)
        var existingCategories = await categoryRepo.ListOrderedAsync(ct);
        if (existingCategories.Count == 0)
        {
            await segmentTemplateService.ApplyTemplateAsync(actor, normalizedSeg, ct);
        }
        await ImportSpreadsheetKitAsync(actor, normalizedSeg, ct);

        if (isAcai)
        {
            var allCats = await categoryRepo.ListOrderedAsync(ct);
            var catFamilia = allCats.FirstOrDefault(c => c.Name.Equals("Explosão Família (Potes)", StringComparison.OrdinalIgnoreCase));
            var curProds = await productRepo.ListAllOrderedAsync(ct);
            if (catFamilia is not null && !curProds.Any(p => p.Name.Equals("Açaí Self-Service por Quilo", StringComparison.OrdinalIgnoreCase)))
            {
                var pKg = await catalogService.CreateProductAsync(actor, catFamilia.Id, "Açaí Self-Service por Quilo", 64.90m, "Pesagem na balança do balcão (preço por kg).", "ACAI-KG", ProductSaleMode.Weight, false, 99, ct);
                if (pKg.IsSuccess)
                {
                    await catalogService.AddProductImageAsync(actor, pKg.Value.Id, "/store-assets/acai/puro-100.jpg", 0, true, ct);
                }
            }

            if ((await promotionRepo.ListActiveCandidatesAsync(ct)).Count == 0)
            {
                var allProdsForPromo = await productRepo.ListAllOrderedAsync(ct);
                var promoTargets = allProdsForPromo
                    .Where(p => p.Name.Equals("Clássico", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Mousse de Morango", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (PromotionTargetType.Product, p.Id))
                    .ToList();
                if (promoTargets.Count > 0)
                {
                    await catalogService.CreatePromotionAsync(actor, "Ofertas Especiais Açaí", PromotionDiscountType.FixedAmount, 2.00m, clock.UtcNow.AddDays(-1), clock.UtcNow.AddYears(2), promoTargets, true, ct);
                }
            }

            if ((await acaiCupRepo.ListAsync(true, ct)).Count == 0)
            {
                await PitakoDevDemoSeeder.SeedAcaiBuilderDefaultsAsync(acaiService, actor, ct);
            }
        }

        // Garantir foto visual padrão de segmento em todos os produtos gerados pelo template universal também
        var allTenantProducts = await productRepo.ListAllOrderedAsync(ct);
        var updatedImg = false;
        foreach (var p in allTenantProducts)
        {
            if (p.Images.Count == 0)
            {
                p.AddImage(BuildSegmentProductArtPath(normalizedSeg, p.Name), 0, true);
                updatedImg = true;
            }
        }
        if (updatedImg) await productRepo.SaveChangesAsync(ct);

        // 3. Salão & QR Mesas
        if ((await sectorRepo.ListOrderedAsync(ct)).Count == 0)
        {
            var secRes = await diningService.CreateSectorAsync(actor, "Salão Principal", null, 1, ct);
            if (secRes.IsSuccess)
            {
                await diningService.CreateTableAsync(actor, secRes.Value.Id, "M01", 4, $"qr-{tenant.Slug}-m01", ct);
                await diningService.CreateTableAsync(actor, secRes.Value.Id, "M02", 4, $"qr-{tenant.Slug}-m02", ct);
            }
        }

        // 4. Totem Autoatendimento
        if ((await totemRepo.ListAllAsync(ct)).Count == 0)
        {
            await totemService.RegisterTerminalAsync(actor, "TOTEM-01", "Totem Salão Principal", TotemFulfillmentChoice.CounterPickup, false, false, 128, null, ct);
        }

        // 5. Política de Frete & Entregador Padrão
        if (await deliveryPolicyRepo.FindEffectivePolicyAsync(null, ct) is null)
        {
            await deliveryPolicyService.CreatePolicyAsync(actor, -20.5386, -47.4008, 12m, FreightCalculationMode.FixedFlat, 6.00m, 3m, 2.00m, 35, 15.00m, 60.00m, null, true, true, ct);
        }
        if ((await courierRepo.ListAllAsync(ct)).Count == 0)
        {
            await courierService.RegisterCourierAsync(actor, "Entregador Padrão 01", "16999112233", CourierVehicleType.Motorcycle, CourierCommissionMode.FixedPerDelivery, 6.00m, 0m, null, null, ct);
        }

        // 6. Impressoras Térmicas 1ª e 2ª Via
        if ((await printerRepo.ListAllAsync(ct)).Count == 0)
        {
            await printingService.RegisterPrinterAsync(actor, "COZINHA-01", "Impressora Cozinha / Produção (1ª Via)", PrinterConnectionKind.NetworkTcpEscPos, "192.168.0.100:9100", PaperWidthMm.Mm80, null, true, null, ct);
            await printingService.RegisterPrinterAsync(actor, "CAIXA-01", "Impressora Caixa / Balcão & Entrega (2ª Via)", PrinterConnectionKind.UsbSpooler, "USB001", PaperWidthMm.Mm80, null, true, null, ct);
        }

        // 7. Cupom Boas-vindas
        if ((await couponRepo.ListAllAsync(ct)).Count == 0)
        {
            await couponsService.CreateCouponAsync(
                actor,
                "PRIMEIRACOMPRA",
                CouponDiscountType.FixedAmount,
                5.00m,
                clock.UtcNow.AddDays(-1),
                clock.UtcNow.AddYears(2),
                minimumOrderAmount: 25.00m,
                maxTotalUses: 200,
                cancellationToken: ct);
        }

        // 8. Canal WhatsApp Bot & IA padrão por loja
        if ((await waRepo.ListAllAsync(ct)).Count == 0)
        {
            await waService.ConfigureChannelAsync(
                actor,
                tenant.TradeName,
                tenant.ContactPhone ?? "5516999000000",
                $"wa-{tenant.Slug}-{tenant.Id.ToString("N")[..6]}",
                $"waba-{tenant.Slug}",
                "wa-cloud-token-configurar",
                "wa-app-secret-configurar",
                $"verify-{tenant.Slug}",
                WhatsAppAutoReplyMode.HybridBotThenAi,
                $"/loja/{tenant.Slug}",
                null,
                ct);
        }
    }

    public async Task<KitImportSummary> ImportSpreadsheetKitAsync(User actor, string segmentCode, CancellationToken ct = default)
    {
        var tenantId = actor.TenantId ?? throw new InvalidOperationException("TenantId obrigatório para importar kit.");
        tenantSetter.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var kit = ResolveKit(segmentCode);

        var existingSupplies = (await supplyRepo.ListAllAsync(ct))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var supplyMap = new Dictionary<string, SupplyItem>(StringComparer.OrdinalIgnoreCase);
        int suppliesCreated = 0;

        foreach (var s in kit.Supplies)
        {
            if (existingSupplies.TryGetValue(s.Name, out var found))
            {
                supplyMap[s.Key] = found;
                continue;
            }

            var created = await inventoryService.CreateSupplyItemAsync(actor, s.Name, s.Price, s.Qty, s.Unit, s.Category, s.Key.ToUpperInvariant(), cancellationToken: ct);
            if (created.IsSuccess)
            {
                supplyMap[s.Key] = created.Value;
                suppliesCreated++;
            }
        }

        var existingCategories = (await categoryRepo.ListOrderedAsync(ct))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existingProducts = (await productRepo.ListAllOrderedAsync(ct))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existingSheets = (await sheetRepo.ListAllAsync(ct)).Where(s => s.ProductId.HasValue).Select(s => s.ProductId!.Value).ToHashSet();

        int productsCreated = 0;
        int sheetsCreated = 0;
        int orderIdx = existingCategories.Count + 1;

        foreach (var prodDef in kit.Products)
        {
            if (!existingCategories.TryGetValue(prodDef.LineName, out var category))
            {
                category = Category.Create(tenantId, prodDef.LineName, $"Produtos da linha {prodDef.LineName}", null, orderIdx++, segmentCode).Value;
                await categoryRepo.AddAsync(category, ct);
                existingCategories[prodDef.LineName] = category;
            }

            if (!existingProducts.TryGetValue(prodDef.Name, out var prod))
            {
                prod = Product.Create(
                    tenantId,
                    category.Id,
                    prodDef.Name,
                    Money.Create(prodDef.Price).Value,
                    prodDef.Description,
                    $"KIT-{productsCreated + 1:D3}",
                    ProductSaleMode.Unit,
                    prodDef.Featured,
                    productsCreated + 1,
                    isNew: productsCreated < 3,
                    isRecommended: prodDef.Featured,
                    tagsCsv: prodDef.Tags,
                    preparationMinutes: 15).Value;

                prod.AddImage(BuildSegmentProductArtPath(segmentCode, prodDef.Name), 0, true);
                await productRepo.AddAsync(prod, ct);
                existingProducts[prodDef.Name] = prod;
                productsCreated++;
            }
            else if (prod.Images.Count == 0)
            {
                prod.AddImage(BuildSegmentProductArtPath(segmentCode, prodDef.Name), 0, true);
            }

            if (!existingSheets.Contains(prod.Id) && prodDef.Recipe.Length > 0)
            {
                var ingInputs = new List<IngredientInput>();
                foreach (var r in prodDef.Recipe)
                {
                    if (supplyMap.TryGetValue(r.SupplyKey, out var sup))
                    {
                        ingInputs.Add(new IngredientInput(sup.Id, r.Qty, r.Unit));
                    }
                }
                if (ingInputs.Count > 0)
                {
                    var pkgInputs = prodDef.PackagingCost > 0m
                        ? new[] { new PackagingInput(null, $"Embalagem & Kit Delivery ({prodDef.SizeLabel})", 1m, "unidade", prodDef.PackagingCost) }
                        : [];

                    var shRes = await inventoryService.CreateTechnicalSheetAsync(
                        actor,
                        $"FT — {prodDef.Name} ({prodDef.SizeLabel})",
                        1m,
                        "unidade",
                        ingInputs,
                        pkgInputs,
                        prod.Id,
                        $"Ficha técnica modelo planilha: {prodDef.SizeLabel}",
                        ct);
                    if (shRes.IsSuccess)
                    {
                        existingSheets.Add(prod.Id);
                        sheetsCreated++;
                    }
                }
            }
        }
        await categoryRepo.SaveChangesAsync(ct);

        var existingCombos = (await comboRepo.ListAllOrderedAsync(ct))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        int combosCreated = 0;
        foreach (var cDef in kit.Combos)
        {
            if (existingCombos.ContainsKey(cDef.Name)) continue;
            var items = cDef.ProductNames.Where(n => existingProducts.ContainsKey(n)).Select(n => existingProducts[n]).ToList();
            if (items.Count == 0) continue;

            var sumPrice = items.Sum(x => x.BasePrice.Amount);
            var comboPrice = Math.Round(sumPrice * (1m - cDef.DiscountPercent), 2);
            var combo = Combo.Create(tenantId, cDef.Name, Money.Create(Math.Max(1m, comboPrice)).Value, cDef.Description, null, true, combosCreated + 1).Value;
            foreach (var it in items)
            {
                combo.AddItem(it.Id, 1);
            }
            await comboRepo.AddAsync(combo, ct);
            combosCreated++;
        }
        if (combosCreated > 0) await comboRepo.SaveChangesAsync(ct);

        if (kit.Promotions.Length > 0)
        {
            var existingPromos = (await promotionRepo.ListAllAsync(ct))
                .Select(pr => pr.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pDef in kit.Promotions)
            {
                if (existingPromos.Contains(pDef.Name)) continue;
                var targets = pDef.ProductNames
                    .Where(n => existingProducts.ContainsKey(n))
                    .Select(n => (PromotionTargetType.Product, existingProducts[n].Id))
                    .ToList();
                if (targets.Count == 0) continue;

                await catalogService.CreatePromotionAsync(
                    actor,
                    pDef.Name,
                    PromotionDiscountType.Percentage,
                    pDef.DiscountPercent,
                    clock.UtcNow.AddDays(-1),
                    clock.UtcNow.AddYears(2),
                    targets,
                    true,
                    ct);
            }
        }

        return new KitImportSummary(suppliesCreated, productsCreated, sheetsCreated, combosCreated);
    }

    public static string BuildSegmentProductArtPath(string? segmentCode, string productName)
    {
        var seg = NormalizeSegmentKey(segmentCode);
        if (seg == "acaiteria")
        {
            var lk = (productName ?? "").ToLowerInvariant();
            if (lk.Contains("garrafa") && lk.Contains("morango")) return "/store-assets/acai/garrafa-morango.jpg";
            if (lk.Contains("garrafa") && lk.Contains("maracuj")) return "/store-assets/acai/garrafa-maracuja.jpg";
            if (lk.Contains("garrafa") && lk.Contains("pa")) return "/store-assets/acai/garrafa-pacoca.jpg";
            if (lk.Contains("garrafa")) return "/store-assets/acai/garrafa-tradicional.jpg";
            if (lk.Contains("mousse") && lk.Contains("morango")) return "/store-assets/acai/mousse-morango.jpg";
            if (lk.Contains("mousse") && lk.Contains("maracuj")) return "/store-assets/acai/mousse-maracuja.jpg";
            if (lk.Contains("mousse") && lk.Contains("pa")) return "/store-assets/acai/mousse-pacoca.jpg";
            if (lk.Contains("mousse")) return "/store-assets/acai/mousse-tradicional.png";
            if (lk.Contains("pistache")) return "/store-assets/acai/pistache.jpg";
            if (lk.Contains("nutella")) return "/store-assets/acai/nutella.jpg";
            if (lk.Contains("oreo")) return "/store-assets/acai/oreo-supreme.jpg";
            return "/store-assets/acai/monte-seu-jeito.jpg";
        }
        var slugPart = string.Concat(productName.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'))
            .Trim('-');
        while (slugPart.Contains("--", StringComparison.Ordinal))
            slugPart = slugPart.Replace("--", "-", StringComparison.Ordinal);
        if (slugPart.Length > 48) slugPart = slugPart[..48];
        if (string.IsNullOrWhiteSpace(slugPart)) slugPart = "item";
        return $"/api/v1/public/segment-art/{seg}/{slugPart}";
    }

    private static string BuildDefaultMonogramLogoDataUrl(string tradeName, string primaryHex, string accentHex)
    {
        var words = tradeName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length >= 2
            ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
            : tradeName.Length >= 2
                ? tradeName[..2].ToUpperInvariant()
                : "LJ";
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 128 128'><rect width='128' height='128' rx='28' fill='{primaryHex}'/><circle cx='98' cy='30' r='12' fill='{accentHex}'/><text x='64' y='78' text-anchor='middle' font-family='Arial,sans-serif' font-size='48' font-weight='900' fill='#FFFFFF'>{initials}</text></svg>";
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }

    public static string NormalizeSegmentKey(string? segmentCode)
    {
        var raw = (segmentCode ?? "outros").Trim().ToLowerInvariant()
            .Replace("ç", "c").Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i").Replace("ó", "o").Replace("ô", "o").Replace("õ", "o").Replace("ú", "u");
        if (string.IsNullOrWhiteSpace(raw)) return "outros";
        if (raw.Contains("acai")) return "acaiteria";
        if (raw.Contains("pizza")) return "pizzaria";
        if (raw.Contains("hamburg") || raw.Contains("burger")) return "hamburgueria";
        if (raw.Contains("pastel")) return "pastelaria";
        if (raw.Contains("sorvet") || raw.Contains("gelat")) return "sorveteria";
        if (raw.Contains("lanche")) return "lanchonete";
        if (raw.Contains("rotiss")) return "rotisseria";
        if (raw.Contains("marmit")) return "marmitex";
        return raw;
    }

    private static SegmentWorkbookKit ResolveKit(string? segmentCode)
    {
        var code = NormalizeSegmentKey(segmentCode);
        return code switch
        {
            "pizzaria" => PizzariaKit(),
            "hamburgueria" => HamburgueriaKit(),
            "lanchonete" => LanchoneteKit(),
            "adega" => AdegaKit(),
            "sorveteria" => SorveteriaKit(),
            "padaria" => PadariaKit(),
            "doceria" => DoceriaKit(),
            "pastelaria" => PastelariaKit(),
            "rotisseria" => RotisseriaKit(),
            "marmitex" => MarmitexKit(),
            "restaurante" => RestauranteKit(),
            "acaiteria" => AcaiteriaKit(),
            _ => UniversalKit()
        };
    }

    private static SegmentWorkbookKit PizzariaKit() => new(
        "#7F1D1D", "#FFF7ED", "#F59E0B",
        "Pizzas artesanais com fermentação lenta e ingredientes selecionados.",
        [
            new("massa", "Farinha de trigo / massa pizza (5kg)", 22m, 5000m, "g", "Massas"),
            new("molho", "Molho de tomate da casa (2kg)", 19m, 2000m, "g", "Molhos"),
            new("mucarela", "Queijo mussarela fatiado (4kg)", 136m, 4000m, "g", "Queijos"),
            new("calabresa", "Calabresa fatiada (2,5kg)", 55m, 2500m, "g", "Frios"),
            new("presunto", "Presunto cozido (2kg)", 44m, 2000m, "g", "Frios"),
            new("quatroqueijos", "Mix provolone, parmesão e gorgonzola (1kg)", 48m, 1000m, "g", "Queijos"),
            new("refri2l", "Refrigerante 2 Litros (6 un)", 43.20m, 6m, "unidade", "Bebidas"),
        ],
        [
            new("Pizzas tradicionais", "Pizza mussarela 8 fatias", "8 fatias", "Pizza de mussarela com molho da casa.", 30.00m, 2.50m, "pizza,tradicional", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 350, "g")]),
            new("Pizzas tradicionais", "Pizza calabresa 8 fatias", "8 fatias", "Pizza de calabresa com cebola.", 32.90m, 2.50m, "pizza,tradicional", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 200, "g"), new("calabresa", 250, "g")]),
            new("Pizzas especiais", "Pizza portuguesa 8 fatias", "8 fatias", "Pizza portuguesa tradicional.", 38.90m, 2.80m, "pizza,especial", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 220, "g"), new("presunto", 160, "g")]),
            new("Pizzas especiais", "Pizza quatro queijos 8 fatias", "8 fatias", "Mussarela, provolone, parmesão e gorgonzola.", 43.90m, 2.80m, "pizza,especial", true, [new("massa", 350, "g"), new("mucarela", 200, "g"), new("quatroqueijos", 200, "g")]),
            new("Pizzas individuais", "Pizza brotinho", "Brotinho", "Pizza individual de mussarela.", 14.90m, 1.00m, "pizza,brotinho", false, [new("massa", 150, "g"), new("molho", 40, "g"), new("mucarela", 140, "g")]),
            new("Bebidas", "Refrigerante 2 Litros", "2 L", "Garrafa 2 L bem gelada.", 13.00m, 0.30m, "bebida", false, [new("refri2l", 1, "unidade")]),
        ],
        [new("Combo família", "Pizza grande + refrigerante 2 L.", 0.12m, ["Pizza mussarela 8 fatias", "Refrigerante 2 Litros"])],
        [new("Noite da pizza", "Pizza calabresa com desconto promocional.", 25m, ["Pizza calabresa 8 fatias"])]);

    private static SegmentWorkbookKit HamburgueriaKit() => new(
        "#18181B", "#FAFAFA", "#EA580C",
        "Smash burgers prensados na hora e artesanais suculentos.",
        [
            new("pao", "Pão de hambúrguer brioche (12 un)", 16.80m, 12m, "unidade", "Pães"),
            new("carne120", "Hambúrguer bovino artesanal (1kg)", 32m, 1000m, "g", "Carnes"),
            new("cheddar", "Queijo cheddar/prato fatiado (96 un)", 48m, 96m, "unidade", "Queijos"),
            new("bacon", "Bacon fatiado crocante (1kg)", 36m, 1000m, "g", "Frios"),
            new("batata", "Batata palito pré-frita (2kg)", 28m, 2000m, "g", "Guarnições"),
            new("lata", "Refrigerante lata 350ml (12 un)", 34.80m, 12m, "unidade", "Bebidas"),
        ],
        [
            new("Hambúrgueres", "Hambúrguer clássico", "120 g", "Pão, carne 120 g, queijo e molho da casa.", 14.90m, 1.50m, "burger,classico", true, [new("pao", 1, "unidade"), new("carne120", 120, "g"), new("cheddar", 1, "unidade")]),
            new("Hambúrgueres", "Cheeseburger bacon", "150 g", "Carne, queijo derretido e bacon.", 19.90m, 1.70m, "burger,bacon", true, [new("pao", 1, "unidade"), new("carne120", 150, "g"), new("cheddar", 2, "unidade"), new("bacon", 35, "g")]),
            new("Hambúrgueres", "X-salada", "120 g", "Clássico com alface, tomate e maionese.", 15.90m, 1.50m, "burger,salada", true, [new("pao", 1, "unidade"), new("carne120", 120, "g"), new("cheddar", 1, "unidade")]),
            new("Porções", "Batata frita 300 g", "300 g", "Porção crocante de batatas.", 9.90m, 1.00m, "porcao,batata", false, [new("batata", 300, "g")]),
            new("Bebidas", "Refrigerante lata 350 ml", "350 ml", "Lata bem gelada.", 6.50m, 0.15m, "bebida", false, [new("lata", 1, "unidade")]),
        ],
        [new("Combo clássico", "Hambúrguer clássico, batata e refrigerante.", 0.15m, ["Hambúrguer clássico", "Batata frita 300 g", "Refrigerante lata 350 ml"])],
        [new("Combo burger em dobro", "Dois hambúrgueres clássicos por preço especial.", 25m, ["Hambúrguer clássico"])]);

    private static SegmentWorkbookKit LanchoneteKit() => new(
        "#0F766E", "#F0FDFA", "#F59E0B",
        "Lanches quentinhos na chapa, hot dogs e sucos naturais na hora.",
        [
            new("paoforma", "Pão de forma tradicional (500g)", 8.50m, 500m, "g", "Pães"),
            new("salsicha", "Salsicha hot dog (1kg)", 18m, 1000m, "g", "Carnes"),
            new("rosbife", "Rosbife fatiado temperado (1kg)", 46m, 1000m, "g", "Frios"),
            new("mucarela", "Queijo muçarela e presunto (2kg)", 64m, 2000m, "g", "Frios"),
            new("polpa", "Polpa de fruta natural (1kg)", 18m, 1000m, "g", "Bebidas"),
        ],
        [
            new("Lanches", "Misto quente", "Unidade", "Pão tostado com presunto e queijo.", 6.90m, 0.50m, "lanche,misto", true, [new("paoforma", 60, "g"), new("mucarela", 60, "g")]),
            new("Lanches", "Cachorro-quente", "Unidade", "Hot dog tradicional com molho e batata palha.", 7.90m, 0.70m, "lanche,hotdog", true, [new("paoforma", 70, "g"), new("salsicha", 90, "g")]),
            new("Lanches", "Bauru", "Unidade", "Sanduíche quente com queijo e tomate.", 11.90m, 0.80m, "lanche,bauru", true, [new("paoforma", 80, "g"), new("rosbife", 80, "g"), new("mucarela", 45, "g")]),
            new("Bebidas", "Suco natural 400 ml", "400 ml", "Suco natural preparado na hora.", 5.90m, 0.60m, "bebida,suco", false, [new("polpa", 100, "g")]),
        ],
        [new("Combo lanche", "Misto quente com suco 300 ml.", 0.12m, ["Misto quente", "Suco natural 400 ml"])],
        [new("Combo intervalo", "Cachorro-quente e suco com preço especial.", 25m, ["Cachorro-quente", "Suco natural 400 ml"])]);

    private static SegmentWorkbookKit AdegaKit() => new(
        "#0F172A", "#F8FAFC", "#8B5CF6",
        "Bebidas geladas, conveniência, kits e entrega rápida.",
        [
            new("agua500", "Água mineral 500 ml (12 un)", 13.20m, 12m, "unidade", "Águas"),
            new("lata", "Refrigerante lata 350 ml (12 un)", 38.40m, 12m, "unidade", "Refrigerantes"),
            new("suco1l", "Suco integral 1 L (6 un)", 45m, 6m, "unidade", "Sucos"),
            new("gelo5", "Gelo filtrado pacote 5 kg (5 un)", 25m, 5m, "unidade", "Conveniência"),
        ],
        [
            new("Águas", "Água mineral 500 ml", "500 ml", "Água mineral gelada.", 2.50m, 0.30m, "agua,bebida", false, [new("agua500", 1, "unidade")]),
            new("Refrigerantes", "Refrigerante lata 350 ml", "350 ml", "Refrigerante em lata 350 ml.", 5.90m, 0.30m, "refri,lata", true, [new("lata", 1, "unidade")]),
            new("Sucos", "Suco integral 1 L", "1 L", "Suco integral de fruta 1 litro.", 12.90m, 0.50m, "suco,integral", true, [new("suco1l", 1, "unidade")]),
            new("Conveniência", "Gelo pacote 5 kg", "5 kg", "Pacote de gelo filtrado.", 8.90m, 0.30m, "gelo,conveniencia", false, [new("gelo5", 1, "unidade")]),
        ],
        [new("Kit resenha sem álcool", "Kit com refrigerantes, águas e gelo.", 0.10m, ["Refrigerante lata 350 ml", "Água mineral 500 ml", "Gelo pacote 5 kg"])],
        [new("Combo gelado econômico", "Oferta de refrigerante com gelo.", 25m, ["Refrigerante lata 350 ml", "Gelo pacote 5 kg"])]);

    private static SegmentWorkbookKit SorveteriaKit() => new(
        "#BE185D", "#FDF2F8", "#F59E0B",
        "Sorvetes cremosos, casquinhas, potes viagem e milk-shakes batidos na hora.",
        [
            new("sorvtrad", "Sorvete massa artesanal (6000g)", 85m, 6000m, "g", "Massas"),
            new("casquinha", "Casquinha crocante (50 un)", 25m, 50m, "unidade", "Complementos"),
            new("cobertura", "Cobertura chocolate/morango (1300g)", 22m, 1300m, "g", "Caldas"),
            new("leite", "Leite integral UHT (12000ml)", 54m, 12000m, "ml", "Leites"),
        ],
        [
            new("Sorvetes", "Casquinha 1 bola", "100 g", "Uma bola de sorvete na casquinha.", 4.90m, 0.30m, "sorvete,casquinha", true, [new("sorvtrad", 100, "g"), new("casquinha", 1, "unidade")]),
            new("Sorvetes", "Copo 300 ml", "300 ml", "Sorvete no copo com cobertura.", 9.90m, 0.70m, "sorvete,copo", true, [new("sorvtrad", 250, "g"), new("cobertura", 30, "g")]),
            new("Milk-shakes", "Milk-shake 400 ml", "400 ml", "Milk-shake cremoso.", 9.90m, 0.90m, "milkshake", true, [new("sorvtrad", 200, "g"), new("leite", 120, "ml"), new("cobertura", 30, "g")]),
            new("Potes para viagem", "Pote de sorvete 1,5 L", "1,5 L", "Pote de sorvete para compartilhar.", 20.90m, 1.00m, "pote,sorvete", false, [new("sorvtrad", 900, "g")]),
        ],
        [new("Combo família", "Dois copos de 300 ml e uma cobertura extra.", 0.10m, ["Copo 300 ml", "Copo 300 ml"])],
        [new("Quarta do milk-shake", "Milk-shake 400 ml com preço especial.", 25m, ["Milk-shake 400 ml"])]);

    private static SegmentWorkbookKit PadariaKit() => new(
        "#78350F", "#FFFBEB", "#D97706",
        "Pães franceses quentinhos, pão de queijo, salgados assados e cafeteria.",
        [
            new("farinha", "Farinha panificação (25kg)", 85m, 25000m, "g", "Panificação"),
            new("polvilho", "Polvilho e queijo pão de queijo (5kg)", 72m, 5000m, "g", "Salgados"),
            new("presuntoqueijo", "Presunto e queijo fatiados (4kg)", 128m, 4000m, "g", "Frios"),
            new("cafe", "Café gourmet pó/grãos (1kg)", 44m, 1000m, "g", "Cafeteria"),
            new("leite", "Leite integral UHT (12000ml)", 54m, 12000m, "ml", "Cafeteria"),
        ],
        [
            new("Pães", "Pão francês 50 g", "50 g", "Pão francês assado diariamente.", 0.90m, 0.05m, "pao,padaria", true, [new("farinha", 50, "g")]),
            new("Salgados", "Pão de queijo unidade", "Unidade", "Pão de queijo quentinho.", 1.90m, 0.10m, "salgado,pao-de-queijo", true, [new("polvilho", 60, "g")]),
            new("Bolos", "Bolo simples - fatia", "Fatia", "Fatia de bolo caseiro.", 3.90m, 0.30m, "bolo,fatia", false, [new("farinha", 60, "g")]),
            new("Salgados", "Croissant presunto e queijo", "Unidade", "Croissant assado e recheado.", 7.50m, 0.50m, "salgado,croissant", true, [new("farinha", 70, "g"), new("presuntoqueijo", 60, "g")]),
            new("Bebidas", "Café com leite 200 ml", "200 ml", "Café com leite.", 2.50m, 0.20m, "cafe,bebida", false, [new("cafe", 12, "g"), new("leite", 140, "ml")]),
        ],
        [new("Café da manhã individual", "Café com leite, pão francês e pão de queijo.", 0.10m, ["Café com leite 200 ml", "Pão francês 50 g", "Pão de queijo unidade"])],
        [new("Oferta do balcão", "Três pães de queijo com preço especial.", 25m, ["Pão de queijo unidade"])]);

    private static SegmentWorkbookKit DoceriaKit() => new(
        "#831843", "#FFF1F2", "#EC4899",
        "Brigadeiros gourmet, brownies, bolos caseiros e pudins artesanais.",
        [
            new("leitecond", "Leite condensado integral (2,5kg)", 29m, 2500m, "g", "Lácteos"),
            new("chocnobre", "Chocolate nobre meio amargo (1kg)", 48m, 1000m, "g", "Chocolates"),
            new("farinha", "Farinha e açúcar confeiteiro (2kg)", 14m, 2000m, "g", "Secos"),
            new("ovos", "Ovos frescos selecionados (30 un)", 24m, 30m, "unidade", "Frios"),
        ],
        [
            new("Doces unitários", "Brigadeiro gourmet", "Unidade", "Brigadeiro artesanal de chocolate.", 2.90m, 0.25m, "doce,brigadeiro", true, [new("leitecond", 20, "g"), new("chocnobre", 8, "g")]),
            new("Brownies", "Brownie tradicional", "Unidade", "Brownie macio de chocolate.", 6.50m, 0.50m, "doce,brownie", true, [new("chocnobre", 35, "g"), new("farinha", 25, "g")]),
            new("Bolos e tortas", "Fatia de bolo de cenoura", "Fatia", "Bolo de cenoura com cobertura de chocolate.", 7.90m, 0.60m, "bolo,cenoura", true, [new("farinha", 55, "g"), new("ovos", 1, "unidade"), new("chocnobre", 25, "g")]),
            new("Sobremesas", "Pudim individual", "Unidade", "Pudim com calda de caramelo.", 5.90m, 0.50m, "sobremesa,pudim", false, [new("leitecond", 80, "g"), new("ovos", 1, "unidade")]),
        ],
        [new("Caixa festa 6 doces", "Caixa com seis doces sortidos.", 0.10m, ["Brigadeiro gourmet", "Brigadeiro gourmet", "Brigadeiro gourmet"])],
        [new("Doce da semana", "Brownie com valor promocional.", 25m, ["Brownie tradicional"])]);

    private static SegmentWorkbookKit PastelariaKit() => new(
        "#9A3412", "#FFFBEB", "#EAB308",
        "Pastéis crocantes fritos na hora e caldo de cana gelado.",
        [
            new("massapastel", "Massa fresca de pastel (2kg)", 22m, 2000m, "g", "Massas"),
            new("carnemoida", "Carne moída refogada (1kg)", 34m, 1000m, "g", "Recheios"),
            new("mucarela", "Queijo muçarela ralado (4kg)", 136m, 4000m, "g", "Recheios"),
            new("caldocana", "Caldo de cana fresco (5000ml)", 18m, 5000m, "ml", "Bebidas"),
        ],
        [
            new("Pastéis", "Pastel de queijo", "80 g recheio", "Pastel crocante recheado com queijo.", 5.90m, 0.50m, "pastel,queijo", true, [new("massapastel", 80, "g"), new("mucarela", 80, "g")]),
            new("Pastéis", "Pastel de carne", "80 g recheio", "Pastel com carne moída temperada.", 6.90m, 0.50m, "pastel,carne", true, [new("massapastel", 80, "g"), new("carnemoida", 80, "g")]),
            new("Pastéis", "Pastel pizza", "Unidade", "Pastel com recheio sabor pizza.", 7.50m, 0.50m, "pastel,pizza", true, [new("massapastel", 80, "g"), new("mucarela", 65, "g")]),
            new("Bebidas", "Caldo de cana 500 ml", "500 ml", "Caldo de cana gelado.", 4.50m, 0.50m, "bebida,caldo", false, [new("caldocana", 500, "ml")]),
        ],
        [new("Combo pastel + caldo", "Pastel de queijo com caldo de cana 300 ml.", 0.10m, ["Pastel de queijo", "Caldo de cana 500 ml"])],
        [new("Quarta do pastel", "Dois pastéis de queijo com preço especial.", 25m, ["Pastel de queijo"])]);

    private static SegmentWorkbookKit RestauranteKit() => new(
        "#065F46", "#ECFDF5", "#F59E0B",
        "Pratos executivos, parmegiana individual, saladas frescas e sucos naturais.",
        [
            new("arroz", "Arroz agulhinha cozido (11kg)", 29m, 11000m, "g", "Grãos"),
            new("feijao", "Feijão carioca cozido (2,5kg)", 8.50m, 2500m, "g", "Grãos"),
            new("frango", "Peito frango filé (1kg)", 21m, 1000m, "g", "Proteínas"),
            new("bife", "Contrafilé bife limpo (1kg)", 44m, 1000m, "g", "Proteínas"),
            new("salada", "Mix salada fresca (1kg)", 9.50m, 1000m, "g", "Saladas"),
            new("polpa", "Polpa de fruta natural (1kg)", 18m, 1000m, "g", "Bebidas"),
        ],
        [
            new("Pratos executivos", "Prato feito frango", "450 g", "PF com frango grelhado, arroz, feijão e salada.", 13.90m, 1.50m, "prato,frango", true, [new("frango", 150, "g"), new("arroz", 150, "g"), new("feijao", 100, "g"), new("salada", 50, "g")]),
            new("Pratos executivos", "Prato feito bife", "450 g", "PF com bife, arroz, feijão e acompanhamento.", 17.90m, 1.50m, "prato,bife", true, [new("bife", 150, "g"), new("arroz", 150, "g"), new("feijao", 100, "g"), new("salada", 50, "g")]),
            new("Pratos especiais", "Parmegiana individual", "500 g", "Filé à parmegiana com arroz e fritas.", 22.90m, 1.80m, "prato,parmegiana", true, [new("frango", 180, "g"), new("arroz", 180, "g")]),
            new("Saladas", "Salada completa", "320 g", "Salada fresca com frango.", 12.90m, 1.00m, "salada,leve", false, [new("salada", 180, "g"), new("frango", 120, "g")]),
            new("Bebidas", "Suco natural 500 ml", "500 ml", "Suco natural preparado na hora.", 6.50m, 0.60m, "bebida,suco", false, [new("polpa", 120, "g")]),
        ],
        [new("Combo executivo", "Prato feito de frango com suco 300 ml.", 0.12m, ["Prato feito frango", "Suco natural 500 ml"])],
        [new("Almoço promocional", "Prato feito de frango em oferta no almoço.", 25m, ["Prato feito frango"])]);

    private static SegmentWorkbookKit RotisseriaKit() => new(
        "#92400E", "#FFFBEB", "#D97706",
        "Assados especiais, lasanhas prontas e acompanhamentos artesanais.",
        [
            new("frango", "Frango inteiro temperado (2,2kg)", 24m, 2200m, "g", "Carnes"),
            new("costela", "Costela bovina janela (1kg)", 34m, 1000m, "g", "Carnes"),
            new("massa", "Massa lasanha + carne + molho (1kg)", 24m, 1000m, "g", "Massas"),
            new("batata", "Batata, cenoura e maionese (1kg)", 12m, 1000m, "g", "Guarnições"),
            new("farofa", "Farofa especial temperada (1kg)", 15m, 1000m, "g", "Guarnições"),
        ],
        [
            new("Assados", "Frango assado inteiro", "Inteiro", "Frango assado inteiro temperado.", 29.90m, 1.50m, "rotisseria,frango", true, [new("frango", 1800, "g")]),
            new("Assados", "Costela assada 500 g", "500 g", "Costela bovina assada - porção 500 g.", 32.90m, 1.50m, "rotisseria,costela", true, [new("costela", 500, "g")]),
            new("Massas e pratos", "Lasanha bolonhesa 500 g", "500 g", "Lasanha pronta para consumo.", 17.90m, 1.20m, "rotisseria,lasanha", true, [new("massa", 500, "g")]),
            new("Acompanhamentos", "Maionese de batata 300 g", "300 g", "Salada de batata com maionese.", 7.50m, 0.60m, "acompanhamento,maionese", false, [new("batata", 300, "g")]),
        ],
        [new("Combo frango + farofa", "Frango assado com farofa para acompanhar.", 0.10m, ["Frango assado inteiro", "Maionese de batata 300 g"])],
        [new("Domingo em família", "Frango assado com acompanhamento em oferta.", 25m, ["Frango assado inteiro"])]);

    private static SegmentWorkbookKit MarmitexKit() => new(
        "#B45309", "#FFFBEB", "#16A34A",
        "Marmitex caseiras quentinhas com variedade diária e entrega rápida.",
        [
            new("arroz", "Arroz branco cozido (5kg)", 26m, 5000m, "g", "Grãos"),
            new("feijao", "Feijão carioca temperado (2kg)", 16m, 2000m, "g", "Grãos"),
            new("bife", "Contrafilé em bifes (1kg)", 44m, 1000m, "g", "Misturas"),
            new("frango", "Peito de frango em filés (1kg)", 21m, 1000m, "g", "Misturas"),
            new("legumes", "Legumes e ovos frescos (2kg)", 18m, 2000m, "g", "Acompanhamentos"),
        ],
        [
            new("Marmitex", "Marmitex frango P", "P", "Marmita pequena com frango, arroz, feijão e guarnição.", 11.90m, 1.20m, "marmitex,frango", true, [new("frango", 100, "g"), new("arroz", 150, "g"), new("feijao", 100, "g")]),
            new("Marmitex", "Marmitex frango G", "G", "Marmita grande com frango e salada.", 15.90m, 1.50m, "marmitex,frango", true, [new("frango", 160, "g"), new("arroz", 220, "g"), new("feijao", 140, "g"), new("legumes", 80, "g")]),
            new("Marmitex", "Marmitex bife G", "G", "Marmita grande com bife e fritas.", 19.90m, 1.50m, "marmitex,bife", true, [new("bife", 150, "g"), new("arroz", 220, "g"), new("feijao", 140, "g")]),
            new("Pratos especiais", "Marmitex strogonoff", "Média", "Strogonoff de frango com arroz e batata palha.", 16.90m, 1.50m, "marmitex,strogonoff", true, [new("frango", 150, "g"), new("arroz", 200, "g")]),
            new("Marmitex", "Marmitex vegetariana", "Média", "Opção sem carne com legumes e omelete.", 12.90m, 1.20m, "marmitex,vegetariana", false, [new("legumes", 200, "g"), new("arroz", 180, "g"), new("feijao", 120, "g")]),
        ],
        [new("Combo almoço 2 pessoas", "Duas marmitas de frango P e dois sucos.", 0.10m, ["Marmitex frango P", "Marmitex frango P"])],
        [new("Sexta econômica", "Marmitex frango P com preço especial.", 25m, ["Marmitex frango P"])]);

    private static SegmentWorkbookKit AcaiteriaKit() => new(
        "#4C1D95", "#FAF5FF", "#F59E0B",
        "Mais que açaí, é puro sabor! Monte do seu jeito e receba super gelado.",
        [
            new("acai", "Polpa de Açaí Premium (10kg)", 150m, 9000m, "g", "Bases"),
            new("ninho", "Leite em Pó Ninho (400g)", 16m, 400m, "g", "Acompanhamentos"),
            new("granola", "Granola Crocante (250g)", 17m, 250m, "g", "Acompanhamentos"),
            new("nutella", "Creme de Avelã Nutella (650g)", 60m, 650m, "g", "Cremes"),
            new("pistache", "Creme Gourmet de Pistache (400g)", 30m, 400m, "g", "Cremes"),
            new("morango", "Morango Fresco (1,32kg)", 40m, 1320m, "g", "Frutas"),
            new("leitecond", "Leite Condensado Integral (2,5kg)", 29m, 2500m, "g", "Cremes"),
        ],
        [
            new("Explosão Clássica", "Clássico", "500 ml", "Açaí cremoso, morangos frescos fatiados, chantilly e leite condensado.", 26.90m, 2.37m, "acai,mais-vendidos", true, [new("acai", 315, "g"), new("morango", 70, "g"), new("leitecond", 40, "g")]),
            new("Explosão Clássica", "Ninho com Morango", "500 ml", "Camadas generosas de açaí com creme de Ninho, morangos frescos e leite em pó.", 25.90m, 2.37m, "acai,mais-vendidos", true, [new("acai", 315, "g"), new("ninho", 40, "g"), new("morango", 70, "g")]),
            new("Explosão Premium", "Pistache", "500 ml", "Açaí cremoso com creme gourmet de pistache, morangos frescos e amendoim.", 30.90m, 2.37m, "acai,especial", true, [new("acai", 315, "g"), new("pistache", 35, "g"), new("morango", 55, "g")]),
            new("Explosão Premium", "Nutella", "500 ml", "Açaí cremoso com Nutella original na borda e topo, morangos e Bis.", 30.90m, 2.37m, "acai,especial", true, [new("acai", 315, "g"), new("nutella", 45, "g"), new("morango", 65, "g")]),
        ],
        [
            new("Combo Casal Clássico + Mousse", "2 Clássicos (500 ml) + 1 Mousse de Maracujá (110 ml).", 0.12m, ["Clássico", "Clássico", "Mousse de Maracujá"]),
            new("Combo Explosão Premium (Oreo + Pistache)", "1 Oreo Supreme + 1 Pistache + 1 Mousse Ninho Trufado.", 0.10m, ["Oreo Supreme", "Pistache", "Mousse Ninho Trufado"]),
            new("Combo Garrafa + Mousse", "1 Açaí na Garrafa – Morango (300 ml) + 1 Mousse de Paçoca (110 ml).", 0.10m, ["Açaí na Garrafa – Morango", "Mousse de Paçoca"]),
            new("Combo Família 1 Litro + 2 Mousses", "1 Açaí Puro 1 Litro + 1 Mousse Ninho Trufado + 1 Mousse de Morango.", 0.12m, ["Açaí Puro com Açúcar (1 Litro)", "Mousse Ninho Trufado", "Mousse de Morango"]),
        ],
        []);

    private static SegmentWorkbookKit UniversalKit() => new(
        "#1E293B", "#F8FAFC", "#0F766E",
        "Cardápio inteligente com ficha técnica completa para o seu negócio.",
        [
            new("base1", "Insumo Base 01 (1kg)", 25m, 1000m, "g", "Matéria-Prima"),
            new("base2", "Insumo Base 02 (1kg)", 35m, 1000m, "g", "Matéria-Prima"),
            new("comp1", "Complemento 01 (1kg)", 18m, 1000m, "g", "Complementos"),
            new("beb1", "Bebida Individual (12 un)", 36m, 12m, "unidade", "Revenda"),
        ],
        [
            new("Linha Entrada", "Produto Padrão P", "P", "Produto padrão P montado com ficha técnica controlada.", 16.90m, 0.85m, "padrao,p", true, [new("base1", 150, "g"), new("base2", 50, "g"), new("comp1", 25, "g")]),
            new("Linha Principal", "Produto Especial M", "M", "Carro-chefe da casa com ingredientes selecionados.", 24.90m, 1.10m, "especial,m", true, [new("base1", 220, "g"), new("base2", 80, "g"), new("comp1", 30, "g")]),
            new("Bebidas", "Bebida Individual 350ml", "350 ml", "Item gelado de revenda rápida.", 6.50m, 0.15m, "bebida", false, [new("beb1", 1, "unidade")]),
        ],
        [new("Combo Individual Especial", "Produto Especial M + Bebida Individual.", 0.10m, ["Produto Especial M", "Bebida Individual 350ml"])],
        []);
}
