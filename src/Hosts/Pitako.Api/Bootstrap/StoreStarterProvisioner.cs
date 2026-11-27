using System.Text;
using System.Text.Json;
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
    ICategoryRepository categoryRepo,
    IProductRepository productRepo,
    IComboRepository comboRepo,
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

    private sealed record SegmentWorkbookKit(
        string PrimaryColor,
        string SecondaryColor,
        string AccentColor,
        string DefaultSlogan,
        KitSupplyDef[] Supplies,
        KitProductDef[] Products,
        KitComboDef[] Combos);

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

        // 1. Visual Identity & Horários publicados com logotipo SVG monograma whitelabel (zero PITAKO)
        var monogramLogo = BuildDefaultMonogramLogoDataUrl(tenant.TradeName, kit.PrimaryColor, kit.AccentColor);
        var defaultHours = JsonSerializer.Serialize(
            Enumerable.Range(0, 7).Select(d => new StoreOperatingHoursEntry(d, true, "08:00", "22:00")));

        await brandingService.UpdateDraftAsync(actor, tenant.Id, new UpdateStoreBrandingDraftCommand(
            monogramLogo,
            null,
            kit.DefaultSlogan,
            kit.PrimaryColor,
            kit.SecondaryColor,
            kit.AccentColor,
            "showcase",
            [new StoreBannerSnapshot($"Promoção Especial — {tenant.TradeName}", monogramLogo, "#combos", 1)],
            defaultHours,
            "Peça direto no nosso cardápio digital com descontos exclusivos.",
            tenant.ContactPhone ?? "16999000000",
            $"@{tenant.Slug}"), ct);

        await brandingService.PublishBrandingAsync(actor, tenant.Id, ct);

        // 2. Motor de Personalização + Planilha Ficha Técnica do Segmento (Insumos + Produtos + FTs + Combos)
        var existingCategories = await categoryRepo.ListOrderedAsync(ct);
        if (existingCategories.Count == 0)
        {
            await segmentTemplateService.ApplyTemplateAsync(actor, tenant.SegmentCode, ct);
        }
        await ImportSpreadsheetKitAsync(actor, tenant.SegmentCode, ct);

        // Garantir foto visual padrão de segmento em todos os produtos gerados pelo template universal também
        var allTenantProducts = await productRepo.ListAllOrderedAsync(ct);
        var updatedImg = false;
        foreach (var p in allTenantProducts)
        {
            if (p.Images.Count == 0)
            {
                p.AddImage(BuildSegmentProductArtPath(tenant.SegmentCode, p.Name), 0, true);
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
            await totemService.RegisterTerminalAsync(actor, "TOTEM-01", "Totem Salão Principal", TotemFulfillmentChoice.CounterPickup, false, false, 120, null, ct);
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

        var existingSupplies = (await supplyRepo.ListAllAsync(ct)).ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
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

        var existingCategories = (await categoryRepo.ListOrderedAsync(ct)).ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var existingProducts = (await productRepo.ListAllOrderedAsync(ct)).ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
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

        var existingCombos = (await comboRepo.ListAllOrderedAsync(ct)).ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
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

        return new KitImportSummary(suppliesCreated, productsCreated, sheetsCreated, combosCreated);
    }

    public static string BuildSegmentProductArtPath(string? segmentCode, string productName)
    {
        var seg = string.IsNullOrWhiteSpace(segmentCode) ? "outros" : segmentCode.Trim().ToLowerInvariant();
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

    private static SegmentWorkbookKit ResolveKit(string? segmentCode)
    {
        var code = (segmentCode ?? "").Trim().ToLowerInvariant();
        return code switch
        {
            "pizzaria" => PizzariaKit(),
            "hamburgueria" => HamburgueriaKit(),
            "adega" => AdegaKit(),
            "sorveteria" => SorveteriaKit(),
            "padaria" => PadariaKit(),
            "doceria" => DoceriaKit(),
            "pastelaria" => PastelariaKit(),
            "restaurante" or "lanchonete" => RestauranteKit(),
            "acaiteria" => AcaiteriaKit(),
            _ => UniversalKit()
        };
    }

    private static SegmentWorkbookKit PizzariaKit() => new(
        "#7F1D1D", "#FFF7ED", "#F59E0B",
        "Pizzas artesanais com fermentação lenta e ingredientes selecionados.",
        [
            new("massa", "Farinha de trigo tipo 1 (5kg)", 22m, 5000m, "g", "Massas"),
            new("molho", "Molho de tomate pelati (2kg)", 19m, 2000m, "g", "Molhos"),
            new("mucarela", "Queijo muçarela fatiado (4kg)", 136m, 4000m, "g", "Queijos"),
            new("catupiry", "Requeijão Catupiry original (1,5kg)", 46m, 1500m, "g", "Queijos"),
            new("calabresa", "Linguiça calabresa fatiada (2,5kg)", 55m, 2500m, "g", "Frios"),
            new("frango", "Peito de frango desfiado (1kg)", 24m, 1000m, "g", "Carnes"),
            new("bacon", "Bacon defumado em cubos (1kg)", 34m, 1000m, "g", "Carnes"),
            new("refri2l", "Refrigerante 2 Litros", 7.20m, 1m, "unidade", "Bebidas"),
        ],
        [
            new("Tradicionais", "Muçarela", "35 cm", "Massa artesanal, molho pelati, muçarela derretida e orégano.", 44.90m, 2.50m, "pizza,tradicional", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 320, "g")]),
            new("Tradicionais", "Calabresa Acebolada", "35 cm", "Massa artesanal, muçarela, calabresa fatiada e azeitonas.", 46.90m, 2.50m, "pizza,tradicional", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 220, "g"), new("calabresa", 200, "g")]),
            new("Especiais", "Frango com Catupiry", "35 cm", "Frango desfiado temperado e cobertura de Catupiry original.", 52.90m, 2.50m, "pizza,especial", true, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 180, "g"), new("frango", 220, "g"), new("catupiry", 150, "g")]),
            new("Especiais", "Bacon Crocante", "35 cm", "Molho de tomate, muçarela e bacon dourado crocante.", 52.90m, 2.50m, "pizza,especial", false, [new("massa", 350, "g"), new("molho", 80, "g"), new("mucarela", 240, "g"), new("bacon", 180, "g")]),
            new("Bebidas", "Refrigerante 2 Litros", "2000 ml", "Garrafa 2L bem gelada.", 13.00m, 0.30m, "bebida", false, [new("refri2l", 1, "unidade")]),
        ],
        [new("Combo Casal (Grande + Refri 2L)", "Calabresa Acebolada 35cm + Refrigerante 2 Litros.", 0.10m, ["Calabresa Acebolada", "Refrigerante 2 Litros"])]);

    private static SegmentWorkbookKit HamburgueriaKit() => new(
        "#18181B", "#FAFAFA", "#EA580C",
        "Smash burgers prensados na hora e artesanais de 160g.",
        [
            new("pao", "Pão brioche selado (12 un)", 16.80m, 12m, "unidade", "Pães"),
            new("carne100", "Blend bovino artesanal (1kg)", 32m, 1000m, "g", "Carnes"),
            new("cheddar", "Queijo cheddar fatiado (96 un)", 48m, 96m, "unidade", "Queijos"),
            new("bacon", "Bacon fatiado crocante (1kg)", 36m, 1000m, "g", "Frios"),
            new("batata", "Batata palito pré-frita (2kg)", 28m, 2000m, "g", "Guarnições"),
            new("lata", "Refrigerante lata 350ml (12 un)", 34.80m, 12m, "unidade", "Bebidas"),
        ],
        [
            new("Linha Smash", "Smash Salad", "100g", "Pão brioche selado, smash burger 100g, queijo cheddar, alface e maionese verde.", 18.90m, 1.50m, "smash,burger", true, [new("pao", 1, "unidade"), new("carne100", 100, "g"), new("cheddar", 1, "unidade")]),
            new("Linha Smash", "Duplo Smash Cheddar", "200g", "2 smash burgers 100g com crosta, dobro de cheddar e molho especial.", 23.90m, 1.50m, "smash,duplo", true, [new("pao", 1, "unidade"), new("carne100", 200, "g"), new("cheddar", 2, "unidade")]),
            new("Artesanais 160g", "Clássico Artesanal 160g", "160g", "Pão brioche, hambúrguer 160g suculento, queijo e bacon.", 27.90m, 1.80m, "artesanal", true, [new("pao", 1, "unidade"), new("carne100", 160, "g"), new("cheddar", 2, "unidade"), new("bacon", 30, "g")]),
            new("Acompanhamentos", "Batata Frita Individual", "180g", "Porção crocante de batata palito.", 11.90m, 0.90m, "fritas", false, [new("batata", 180, "g")]),
            new("Bebidas", "Refrigerante Lata 350ml", "350ml", "Lata gelada.", 6.50m, 0.15m, "bebida", false, [new("lata", 1, "unidade")]),
        ],
        [new("Combo Smash Individual", "Smash Salad + Batata Frita + Refrigerante Lata.", 0.10m, ["Smash Salad", "Batata Frita Individual", "Refrigerante Lata 350ml"])]);

    private static SegmentWorkbookKit AdegaKit() => new(
        "#0F172A", "#F8FAFC", "#8B5CF6",
        "Copões 700ml, combos de destilados e cervejas trincando.",
        [
            new("redlabel", "Whisky Red Label (1000ml)", 89m, 1000m, "ml", "Destilados"),
            new("jack", "Whisky Jack Daniel's (1000ml)", 145m, 1000m, "ml", "Destilados"),
            new("baly", "Energético Baly 2L (12000ml)", 48m, 12000m, "ml", "Energéticos"),
            new("redbull", "Energético Red Bull 250ml (24un)", 168m, 24m, "unidade", "Energéticos"),
            new("gelosabor", "Gelo de Sabor (30un)", 45m, 30m, "unidade", "Gelos"),
            new("heinekenln", "Pack Heineken Long Neck (6un)", 37.80m, 6m, "unidade", "Cervejas"),
        ],
        [
            new("Copões 700ml", "Copão Red Label + Baly", "700 ml", "Dose 80ml Whisky Red Label, Baly gelado e 1 gelo de sabor.", 22.90m, 0.80m, "copao,whisky", true, [new("redlabel", 80, "ml"), new("baly", 300, "ml"), new("gelosabor", 1, "unidade")]),
            new("Copões 700ml", "Copão Jack Daniel's + Red Bull", "700 ml", "Dose 80ml Jack Daniel's e 1 lata Red Bull 250ml.", 32.90m, 0.80m, "copao,jack", true, [new("jack", 80, "ml"), new("redbull", 1, "unidade")]),
            new("Cervejas Geladas", "Pack Heineken Long Neck (6 un)", "1980 ml", "Pack com 6 long necks geladas.", 49.90m, 0.50m, "cerveja", true, [new("heinekenln", 6, "unidade")]),
        ],
        [new("Combo Esquenta (2 Copões Red Label)", "2 Copões Red Label + Baly com gelo sabor.", 0.10m, ["Copão Red Label + Baly", "Copão Red Label + Baly"])]);

    private static SegmentWorkbookKit SorveteriaKit() => new(
        "#BE185D", "#FDF2F8", "#F59E0B",
        "Sorvetes cremosos, potes térmicos e milk-shakes batidos na hora.",
        [
            new("sorvtrad", "Sorvete massa tradicional (6000g)", 68m, 6000m, "g", "Massas"),
            new("sorvesp", "Sorvete trufado especial (6000g)", 86m, 6000m, "g", "Massas"),
            new("nutella", "Creme Nutella original (3000g)", 129m, 3000m, "g", "Cremes"),
            new("calda", "Calda quente chocolate/morango (1300g)", 19.50m, 1300m, "g", "Caldas"),
            new("leite", "Leite integral UHT (12000ml)", 54m, 12000m, "ml", "Leites"),
        ],
        [
            new("Taças e Copos", "Sundae Clássico 300ml", "300 ml", "2 bolas de sorvete, calda quente e castanha.", 15.90m, 1.10m, "sorvete,sundae", true, [new("sorvtrad", 180, "g"), new("calda", 30, "g")]),
            new("Milk-Shakes", "Milk-Shake Nutella Oreo 400ml", "400 ml", "Sorvete especial batido com Nutella e leite.", 21.90m, 1.20m, "milkshake", true, [new("sorvesp", 250, "g"), new("nutella", 35, "g"), new("leite", 60, "ml")]),
            new("Potes Viagem", "Pote Térmico 1 Litro", "1000 ml", "Pote térmico de 1L (600g) com até 3 sabores.", 34.90m, 1.80m, "pote", true, [new("sorvesp", 600, "g")]),
        ],
        [new("Combo Milk-Shake + Sundae", "1 Milk-Shake Nutella Oreo + 1 Sundae Clássico.", 0.10m, ["Milk-Shake Nutella Oreo 400ml", "Sundae Clássico 300ml"])]);

    private static SegmentWorkbookKit PadariaKit() => new(
        "#78350F", "#FFFBEB", "#D97706",
        "Pães quentinhos, salgados assados e cafeteria artesanal.",
        [
            new("farinha", "Farinha panificação (25kg)", 85m, 25000m, "g", "Panificação"),
            new("mucarela", "Queijo muçarela fatiado (4kg)", 136m, 4000m, "g", "Frios"),
            new("presunto", "Presunto cozido (3,5kg)", 73.50m, 3500m, "g", "Frios"),
            new("frango", "Peito frango desfiado (1kg)", 24m, 1000m, "g", "Salgados"),
            new("cafe", "Café gourmet grãos/pó (1kg)", 44m, 1000m, "g", "Cafeteria"),
            new("leite", "Leite integral UHT (12000ml)", 54m, 12000m, "ml", "Cafeteria"),
        ],
        [
            new("Pães", "Pão Francês Quentinho (6 un)", "330 g", "Pacote com 6 pães crocantes assados na hora.", 6.50m, 0.25m, "pao,padaria", true, [new("farinha", 240, "g")]),
            new("Lanches na Chapa", "Misto Quente Caprichado", "180 g", "Pão tostado com presunto e muçarela.", 11.90m, 0.50m, "misto,chapa", true, [new("farinha", 60, "g"), new("presunto", 50, "g"), new("mucarela", 50, "g")]),
            new("Salgados", "Coxinha Frango com Catupiry", "140 g", "Massa cremosa recheada com frango desfiado.", 8.50m, 0.40m, "salgado,coxinha", true, [new("farinha", 50, "g"), new("frango", 60, "g")]),
            new("Cafeteria", "Café com Leite Cremoso 240ml", "240 ml", "Café coado com leite vaporizado.", 7.50m, 0.60m, "cafe", false, [new("cafe", 15, "g"), new("leite", 160, "ml")]),
        ],
        [new("Combo Café da Manhã", "Misto Quente Caprichado + Café com Leite Cremoso 240ml.", 0.10m, ["Misto Quente Caprichado", "Café com Leite Cremoso 240ml"])]);

    private static SegmentWorkbookKit DoceriaKit() => new(
        "#831843", "#FFF1F2", "#EC4899",
        "Bolos no pote, copos da felicidade e brigadeiros gourmet.",
        [
            new("leitecond", "Leite condensado integral (2,5kg)", 29m, 2500m, "g", "Lácteos"),
            new("cremeleite", "Creme de leite UHT (1kg)", 14.50m, 1000m, "g", "Lácteos"),
            new("ninho", "Leite Ninho em pó (800g)", 38m, 800m, "g", "Lácteos"),
            new("chocnobre", "Chocolate nobre (1kg)", 48m, 1000m, "g", "Chocolates"),
            new("nutella", "Creme Nutella original (3kg)", 129m, 3000m, "g", "Chocolates"),
            new("morango", "Morango fresco selecionado (1,2kg)", 28m, 1200m, "g", "Frutas"),
        ],
        [
            new("Bolos no Pote", "Bolo no Pote Ninho c/ Morango 250ml", "250 ml", "Massa fofinha, creme de Leite Ninho e morangos frescos.", 14.90m, 1.30m, "bolo,pote", true, [new("leitecond", 80, "g"), new("cremeleite", 40, "g"), new("ninho", 25, "g"), new("morango", 45, "g")]),
            new("Bolos no Pote", "Bolo no Pote Brigadeiro Belga 250ml", "250 ml", "Massa de cacau e brigadeiro belga cremoso.", 14.90m, 1.30m, "bolo,brigadeiro", true, [new("leitecond", 90, "g"), new("cremeleite", 40, "g"), new("chocnobre", 25, "g")]),
            new("Copo da Felicidade", "Copo Felicidade Kinder / Nutella 300ml", "300 ml", "Camadas de creme de Ninho, Nutella pura e morangos.", 21.90m, 1.50m, "copo,nutella", true, [new("leitecond", 80, "g"), new("ninho", 25, "g"), new("nutella", 35, "g"), new("morango", 45, "g")]),
        ],
        [new("Combo Doce Casal", "Bolo no Pote Ninho c/ Morango + Bolo no Pote Brigadeiro Belga.", 0.10m, ["Bolo no Pote Ninho c/ Morango 250ml", "Bolo no Pote Brigadeiro Belga 250ml"])]);

    private static SegmentWorkbookKit PastelariaKit() => new(
        "#9A3412", "#FFFBEB", "#EAB308",
        "Pastéis artesanais de 20cm recheados e fritos na hora.",
        [
            new("massapastel", "Massa fresca de pastel (2kg)", 22m, 2000m, "g", "Massas"),
            new("carnemoida", "Carne moída refogada (1kg)", 34m, 1000m, "g", "Recheios"),
            new("mucarela", "Queijo muçarela ralado (4kg)", 136m, 4000m, "g", "Recheios"),
            new("frango", "Peito frango desfiado (1kg)", 25m, 1000m, "g", "Recheios"),
            new("catupiry", "Catupiry original (1,5kg)", 44m, 1500m, "g", "Recheios"),
            new("lata", "Refrigerante lata / Caldo (12 un)", 34.80m, 12m, "unidade", "Bebidas"),
        ],
        [
            new("Tradicionais 20cm", "Pastel de Carne Tradicional", "20 cm", "Massa crocante recheada com carne moída temperada.", 12.90m, 0.55m, "pastel,carne", true, [new("massapastel", 90, "g"), new("carnemoida", 130, "g")]),
            new("Tradicionais 20cm", "Pastel de Queijo Muçarela", "20 cm", "Muito queijo muçarela derretido e orégano.", 12.90m, 0.55m, "pastel,queijo", true, [new("massapastel", 90, "g"), new("mucarela", 130, "g")]),
            new("Especiais 20cm", "Pastel Frango com Catupiry", "20 cm", "Frango desfiado temperado com Catupiry original.", 15.90m, 0.55m, "pastel,frango", true, [new("massapastel", 90, "g"), new("frango", 110, "g"), new("catupiry", 60, "g")]),
            new("Bebidas", "Refrigerante Lata 350ml", "350 ml", "Lata bem gelada.", 6.50m, 0.15m, "bebida", false, [new("lata", 1, "unidade")]),
        ],
        [new("Combo Lanche Pastel", "1 Pastel de Carne + 1 Pastel de Queijo + Refri Lata.", 0.10m, ["Pastel de Carne Tradicional", "Pastel de Queijo Muçarela", "Refrigerante Lata 350ml"])]);

    private static SegmentWorkbookKit RestauranteKit() => new(
        "#065F46", "#ECFDF5", "#F59E0B",
        "Marmitex executivas e pratos preparados na hora.",
        [
            new("arroz", "Arroz agulhinha cozido (11kg)", 29m, 11000m, "g", "Grãos"),
            new("feijao", "Feijão carioca cozido (2,5kg)", 8.50m, 2500m, "g", "Grãos"),
            new("frango", "Peito frango filé (1kg)", 21m, 1000m, "g", "Proteínas"),
            new("bife", "Contrafilé bife limpo (1kg)", 44m, 1000m, "g", "Proteínas"),
            new("salada", "Mix salada fresca (1kg)", 9.50m, 1000m, "g", "Saladas"),
            new("lata", "Refrigerante lata 350ml (12 un)", 34.80m, 12m, "unidade", "Bebidas"),
        ],
        [
            new("Marmitex Almoço", "Marmitex Média Frango Grelhado 600g", "600 g", "Arroz, feijão, filé de frango grelhado e salada.", 18.90m, 1.15m, "marmitex,frango", true, [new("arroz", 200, "g"), new("feijao", 120, "g"), new("frango", 140, "g"), new("salada", 80, "g")]),
            new("Marmitex Almoço", "Marmitex Média Bife Acebolado 600g", "600 g", "Arroz, feijão, bife acebolado e salada fresca.", 21.90m, 1.15m, "marmitex,bife", true, [new("arroz", 200, "g"), new("feijao", 120, "g"), new("bife", 130, "g"), new("salada", 80, "g")]),
            new("Bebidas", "Refrigerante Lata 350ml", "350 ml", "Lata bem gelada.", 6.50m, 0.15m, "bebida", false, [new("lata", 1, "unidade")]),
        ],
        [new("Combo Almoço Frango + Refri", "Marmitex Média Frango Grelhado + Refrigerante Lata.", 0.10m, ["Marmitex Média Frango Grelhado 600g", "Refrigerante Lata 350ml"])]);

    private static SegmentWorkbookKit AcaiteriaKit() => new(
        "#4C1D95", "#FAF5FF", "#F59E0B",
        "Açaí puro batido na hora com cremes e frutas frescas.",
        [
            new("acai", "Polpa de Açaí Premium (10kg)", 180m, 10000m, "g", "Bases"),
            new("ninho", "Leite em Pó Ninho (1kg)", 38m, 1000m, "g", "Acompanhamentos"),
            new("granola", "Granola Crocante (2kg)", 28m, 2000m, "g", "Acompanhamentos"),
            new("nutella", "Creme de Avelã Nutella (3kg)", 129m, 3000m, "g", "Cremes"),
            new("morango", "Morango Fresco (1,2kg)", 28m, 1200m, "g", "Frutas"),
        ],
        [
            new("Mais Vendidos", "Açaí Clássico 500ml", "500 ml", "Açaí cremoso batido na hora com leite em pó e granola.", 21.90m, 1.20m, "acai,mais-vendidos", true, [new("acai", 320, "g"), new("ninho", 30, "g"), new("granola", 30, "g")]),
            new("Mais Vendidos", "Pistache / Nutella Supremo 300ml", "300 ml", "Copo especial com creme de Nutella e morango fresco.", 24.90m, 1.10m, "acai,especial", true, [new("acai", 200, "g"), new("nutella", 35, "g"), new("morango", 40, "g")]),
        ],
        [new("Combo Duplo Açaí 500ml", "2 Açaís Clássicos 500ml.", 0.10m, ["Açaí Clássico 500ml", "Açaí Clássico 500ml"])]);

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
        [new("Combo Individual Especial", "Produto Especial M + Bebida Individual.", 0.10m, ["Produto Especial M", "Bebida Individual 350ml"])]);
}
