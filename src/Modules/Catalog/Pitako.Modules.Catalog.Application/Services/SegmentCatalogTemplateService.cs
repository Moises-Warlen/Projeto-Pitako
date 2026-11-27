using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Application.Services;

public sealed record AppliedSegmentTemplateSummary(
    string SegmentCode,
    int CategoriesCreated,
    int ProductsCreated,
    int ModifierGroupsCreated);

/// <summary>
/// Aplica modelos completos de configuração por segmento comercial sobre o motor universal de personalização,
/// permitindo ao estabelecimento iniciar imediatamente e personalizar tudo livremente pelo painel.
/// </summary>
public sealed class SegmentCatalogTemplateService
{
    private readonly ICategoryRepository _categoryRepo;
    private readonly IProductRepository _productRepo;
    private readonly IModifierGroupRepository _modifierRepo;
    private readonly ITenantContext _tenantContext;

    public SegmentCatalogTemplateService(
        ICategoryRepository categoryRepo,
        IProductRepository productRepo,
        IModifierGroupRepository modifierRepo,
        ITenantContext tenantContext)
    {
        _categoryRepo = categoryRepo;
        _productRepo = productRepo;
        _modifierRepo = modifierRepo;
        _tenantContext = tenantContext;
    }

    public async Task<Result<AppliedSegmentTemplateSummary>> ApplyTemplateAsync(
        User actor,
        string segmentCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de aplicar modelo em outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Catalog, PermissionAction.Create))
            return Result.Failure<AppliedSegmentTemplateSummary>(Error.Forbidden("Catalog.Forbidden", "Permissão insuficiente para aplicar modelos."));

        var rawCode = (segmentCode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(rawCode))
            return Result.Failure<AppliedSegmentTemplateSummary>(Error.Validation("Catalog.SegmentRequired", "Segmento é obrigatório."));

        var code = NormalizeSegmentKey(rawCode);

        return code switch
        {
            "acaiteria" => await ApplyAcaiteriaAsync(tenantId, cancellationToken),
            "pizzaria" => await ApplyPizzariaAsync(tenantId, cancellationToken),
            "hamburgueria" => await ApplyHamburgueriaAsync(tenantId, cancellationToken),
            "pastelaria" => await ApplyPastelariaAsync(tenantId, cancellationToken),
            "sorveteria" => await ApplySorveteriaAsync(tenantId, cancellationToken),
            "adega" => await ApplyAdegaAsync(tenantId, cancellationToken),
            "padaria" => await ApplyPadariaAsync(tenantId, cancellationToken),
            "doceria" => await ApplyDoceriaAsync(tenantId, cancellationToken),
            "restaurante" or "lanchonete" => await ApplyRestauranteLanchoneteAsync(tenantId, code, cancellationToken),
            _ => await ApplyGenericFoodAsync(tenantId, code, cancellationToken)
        };
    }

    private static string NormalizeSegmentKey(string raw)
    {
        var s = raw.Replace("ç", "c").Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e").Replace("í", "i").Replace("ó", "o").Replace("ô", "o").Replace("õ", "o").Replace("ú", "u");
        if (s.Contains("acai")) return "acaiteria";
        if (s.Contains("pizza")) return "pizzaria";
        if (s.Contains("hamburg") || s.Contains("burger")) return "hamburgueria";
        if (s.Contains("pastel")) return "pastelaria";
        if (s.Contains("sorvet") || s.Contains("gelat")) return "sorveteria";
        return s;
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyAcaiteriaAsync(Guid tenantId, CancellationToken ct)
    {
        var catMonte = Category.Create(tenantId, "Monte seu Açaí", "Use sua criatividade e monte do seu jeito!", null, 1, "acaiteria", "Tamanhos com acompanhamentos grátis configuráveis").Value;
        var catClassica = Category.Create(tenantId, "Explosão Clássica", "Clássicos que nunca saem de moda, cremosos e equilibrados.", null, 2, "acaiteria").Value;
        var catPremium = Category.Create(tenantId, "Explosão Premium", "Combinações irresistíveis com chocolates nobres, cremes especiais e frutas frescas.", null, 3, "acaiteria").Value;
        var catKids = Category.Create(tenantId, "Explosão Kids", "Diversão e sabor em cada colherada! Feitas para a alegria da criançada.", null, 4, "acaiteria").Value;
        var catFitness = Category.Create(tenantId, "Linha Leve & Fitness", "Zero açúcar, Whey Protein e ingredientes funcionais para sua rotina.", null, 5, "acaiteria").Value;
        var catNatural = Category.Create(tenantId, "Explosão Natural", "Açaí pura polpa ou com mix completo de frutas frescas fatiadas.", null, 6, "acaiteria").Value;
        var catGarrafa = Category.Create(tenantId, "Açaí na Garrafa", "Pronto para levar! Garrafas de 300 ml super cremosas para qualquer hora do dia.", null, 7, "acaiteria").Value;
        var catFamilia = Category.Create(tenantId, "Explosão Família (Potes)", "Potes econômicos de 1 Litro e 2 Litros para compartilhar em casa.", null, 8, "acaiteria").Value;
        var catMousses = Category.Create(tenantId, "Mousses & Sobremesas", "Mousses cremosos em potes individuais de 110 ml.", null, 9, "acaiteria").Value;

        foreach (var c in new[] { catMonte, catClassica, catPremium, catKids, catFitness, catNatural, catGarrafa, catFamilia, catMousses })
        {
            await _categoryRepo.AddAsync(c, ct);
        }

        var grpBase = CreateGroupWithCost(tenantId, "Base do Açaí", ModifierGroupType.Complement, 1, 1, true, 1, ModifierGroupKind.RequiredChoice, 1, OptionCountingMode.ByDistinctOption, "cremes", [
            ("Açaí Tradicional Cremoso", 0m, 0m, 1, "Batido artesanal puro"),
            ("Açaí Zero Açúcar", 3.00m, 1.50m, 1, "Versão leve sem xarope")
        ]);

        var grpGratis = CreateGroupWithCost(tenantId, "Acompanhamentos Inclusos", ModifierGroupType.Complement, 0, 7, false, 2, ModifierGroupKind.FreeIncluded, 3, OptionCountingMode.ByTotalQuantity, "acompanhamentos", [
            ("Leite Ninho em pó", 0m, 0.80m, 2, "Porção 25g"),
            ("Leite condensado", 0m, 0.70m, 2, "Porção 25g"),
            ("Granola tradicional", 0m, 1.10m, 2, "Porção 30g"),
            ("Paçoca", 0m, 0.68m, 2, "Porção 25g"),
            ("Amendoim triturado", 0m, 1.00m, 2, "Porção 25g"),
            ("Chocobol", 0m, 1.05m, 2, "Porção 25g"),
            ("Chocolate granulado", 0m, 1.20m, 2, "Porção 25g"),
            ("Confete", 0m, 1.10m, 2, "Porção 25g"),
            ("Sucrilhos", 0m, 0.63m, 2, "Porção 25g"),
            ("Ovomaltine", 0m, 1.33m, 2, "Porção 25g"),
            ("Mel puro", 0m, 2.00m, 2, "Porção 20ml"),
            ("Bis picado", 0m, 1.00m, 2, "Porção 2 un"),
            ("Mini Fini", 0m, 0.90m, 2, "Porção 20g")
        ]);

        var grpFrutas = CreateGroupWithCost(tenantId, "Frutas Frescas", ModifierGroupType.Addon, 0, 8, false, 3, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "frutas", [
            ("Morango", 4.50m, 1.36m, 3, "Porção 45g fresca fatiada"),
            ("Banana", 3.90m, 0.32m, 3, "Porção 40g em rodelas"),
            ("Kiwi", 4.00m, 1.40m, 3, "Porção 40g fatiado"),
            ("Manga", 3.90m, 0.28m, 3, "Porção 40g em cubos"),
            ("Uva sem caroço", 3.50m, 0.88m, 3, "Porção 40g selecionada"),
            ("Maracujá", 3.00m, 0.34m, 3, "Polpa natural 40g")
        ]);

        var grpExtras = CreateGroupWithCost(tenantId, "Adicionais Extras", ModifierGroupType.Addon, 0, 12, false, 4, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "adicionais", [
            ("Nutella original", 6.50m, 2.77m, 3, "Porção 30g"),
            ("Creme gourmet de pistache", 6.00m, 2.25m, 3, "Porção 30g"),
            ("Leite Ninho em pó", 3.50m, 1.00m, 3, "Porção 25g"),
            ("Chocolate com avelã", 4.00m, 1.34m, 3, "Porção 30g"),
            ("Brigadeiro cremoso", 4.00m, 1.25m, 3, "Porção 30g"),
            ("Leite condensado cremoso", 2.50m, 0.70m, 3, "Porção 30ml"),
            ("Chantilly aerado", 3.00m, 0.58m, 3, "Porção 25ml"),
            ("Granola tradicional", 4.00m, 2.04m, 3, "Porção 30g"),
            ("Paçoca rolha esfarelada", 3.00m, 0.68m, 3, "Porção 25g"),
            ("Amendoim triturado", 3.00m, 1.00m, 3, "Porção 25g"),
            ("Sucrilhos crocante", 3.00m, 0.63m, 3, "Porção 25g"),
            ("Ovomaltine crocante", 4.00m, 1.33m, 3, "Porção 25g"),
            ("Bolacha Oreo triturada", 3.50m, 1.10m, 3, "Porção 18g"),
            ("Bis crocante (2 un)", 3.00m, 1.00m, 3, "2 unidades"),
            ("Kit Kat (1 un)", 6.50m, 3.80m, 3, "40g unidade"),
            ("Sonho de Valsa (1 un)", 4.00m, 2.00m, 3, "Bombom picado"),
            ("Gotas de chocolate branco", 3.50m, 0.98m, 3, "Porção 25g"),
            ("Chocobol crocante", 3.50m, 1.05m, 3, "Porção 25g"),
            ("Chocolate granulado", 3.50m, 1.20m, 3, "Porção 25g"),
            ("Confetes coloridos", 3.50m, 1.10m, 3, "Porção 25g"),
            ("Mini Fini confeitos", 3.50m, 0.90m, 3, "Porção 20g"),
            ("Cobertura Fini", 2.50m, 0.38m, 3, "Porção 25ml"),
            ("Mel puro de abelha", 4.00m, 2.00m, 3, "Porção 20ml")
        ]);

        var grpRemocao = CreateGroupWithCost(tenantId, "Preferências / Observações Rápidas", ModifierGroupType.Complement, 0, 3, false, 5, ModifierGroupKind.IngredientRemoval, 3, OptionCountingMode.ByDistinctOption, "remocoes", [
            ("Sem colher descartável", 0m, 0m, 1, null),
            ("Caprichar no leite condensado", 0m, 0m, 1, null),
            ("Enviar separadamente quando possível", 0m, 0m, 1, null)
        ]);

        foreach (var g in new[] { grpBase, grpGratis, grpFrutas, grpExtras, grpRemocao })
        {
            await _modifierRepo.AddAsync(g, ct);
        }

        var prodCount = 0;

        Product CreateAcaiItem(
            Guid catId,
            string name,
            decimal price,
            string desc,
            string sku,
            string imagePath,
            bool isFeatured,
            bool isNew,
            decimal discountAmount,
            (string Name, decimal Price, decimal Cost, decimal? Ml, int FreeCount, string Desc)[] sizes,
            bool attachFreeToppings = true,
            bool attachBase = false,
            string? customKey = null)
        {
            var prod = Product.Create(
                tenantId,
                catId,
                name,
                Money.Create(price).Value,
                desc,
                sku,
                ProductSaleMode.Unit,
                isFeatured: isFeatured,
                displayOrder: ++prodCount,
                isNew: isNew,
                isRecommended: isFeatured,
                pricingStrategy: ProductPricingStrategy.FixedPlusAddons,
                tagsCsv: "acaiteria" + (isFeatured ? ",mais-vendidos" : "") + (isNew ? ",novidade" : "") + (discountAmount > 0m ? ",promocao" : ""),
                preparationMinutes: 7,
                customTemplateKey: customKey ?? "acaiteria-copo").Value;

            prod.AddImage(imagePath, 0, true);

            var vOrder = 1;
            var addedVariations = new List<(ProductVariation Variation, int FreeCount)>();
            foreach (var s in sizes)
            {
                var v = prod.AddVariation(
                    s.Name,
                    Money.Create(s.Price).Value,
                    $"{sku}-{vOrder}",
                    vOrder++,
                    s.Desc,
                    s.Ml,
                    "ml",
                    1,
                    Money.Create(s.Cost).Value).Value;
                addedVariations.Add((v, s.FreeCount));
            }

            var grpOrder = 1;
            if (attachBase) prod.AttachModifierGroup(grpBase.Id, grpOrder++);
            if (attachFreeToppings) prod.AttachModifierGroup(grpGratis.Id, grpOrder++);
            prod.AttachModifierGroup(grpFrutas.Id, grpOrder++);
            prod.AttachModifierGroup(grpExtras.Id, grpOrder++);
            prod.AttachModifierGroup(grpRemocao.Id, grpOrder);

            if (attachFreeToppings)
            {
                foreach (var (v, freeCount) in addedVariations)
                {
                    prod.SetVariationRule(v.Id, grpGratis.Id, 0, freeCount, freeCount, false);
                    prod.SetVariationRule(v.Id, grpFrutas.Id, 0, 6, 0, false);
                    prod.SetVariationRule(v.Id, grpExtras.Id, 0, 10, 0, false);
                }
            }

            return prod;
        }

        // 1. Clássico primeiro na ordem global de produto para quick-add sem obrigatórios; categorias continuam ordenadas por cat.DisplayOrder
        var products = new List<Product>
        {
            // Explosão Clássica
            CreateAcaiItem(
                catClassica.Id,
                "Clássico",
                19.90m,
                "Açaí cremoso, morangos frescos fatiados, chantilly aerado, leite condensado e Nutella na decoração.",
                "ACAI-CLASSICO",
                "/store-assets/acai/classico.jpg",
                isFeatured: true,
                isNew: false,
                discountAmount: 2.00m,
                [
                    ("300 ml", 19.90m, 8.20m, 300m, 3, "2 camadas cremosas"),
                    ("500 ml", 26.90m, 11.40m, 500m, 4, "3 camadas cremosas"),
                    ("700 ml", 33.90m, 14.90m, 700m, 5, "3 camadas generosas")
                ]),

            // Monte seu Açaí
            CreateAcaiItem(
                catMonte.Id,
                "Monte do Seu Jeito",
                15.90m,
                "Use sua criatividade e monte do seu jeito! Escolha o tamanho do copo e personalize os acompanhamentos inclusos, frutas e adicionais.",
                "ACAI-MONTE",
                "/store-assets/acai/monte-seu-jeito.jpg",
                isFeatured: true,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 15.90m, 6.80m, 300m, 3, "Até 3 acompanhamentos inclusos"),
                    ("500 ml", 19.90m, 9.40m, 500m, 4, "Até 4 acompanhamentos inclusos"),
                    ("700 ml", 26.90m, 12.50m, 700m, 5, "Até 5 acompanhamentos inclusos"),
                    ("1 Litro", 36.90m, 18.00m, 1000m, 7, "Até 7 acompanhamentos inclusos")
                ],
                attachFreeToppings: true,
                attachBase: true,
                customKey: "acaiteria-monte"),

            CreateAcaiItem(
                catClassica.Id,
                "Ninho com Morango",
                19.90m,
                "Camadas generosas de açaí com creme de Ninho cremoso, morangos frescos e leite em pó.",
                "ACAI-NINHO-MOR",
                "/store-assets/acai/ninho-morango.jpg",
                isFeatured: true,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 19.90m, 8.10m, 300m, 3, "2 camadas cremosas"),
                    ("500 ml", 25.90m, 11.20m, 500m, 4, "3 camadas cremosas"),
                    ("700 ml", 32.90m, 14.60m, 700m, 5, "3 camadas generosas")
                ]),

            CreateAcaiItem(
                catClassica.Id,
                "Granola",
                19.90m,
                "Açaí tradicional acompanhado de banana em rodelas, morangos frescos, granola crocante e mel puro.",
                "ACAI-GRANOLA",
                "/store-assets/acai/granola.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 19.90m, 8.30m, 300m, 3, "2 camadas cremosas"),
                    ("500 ml", 26.90m, 11.50m, 500m, 4, "3 camadas cremosas"),
                    ("700 ml", 33.90m, 14.90m, 700m, 5, "3 camadas generosas")
                ]),

            // 3. Explosão Premium
            CreateAcaiItem(
                catPremium.Id,
                "Pistache",
                23.90m,
                "Açaí cremoso com creme gourmet de pistache nas bordas e topo, morangos frescos e amendoim triturado crocante.",
                "ACAI-PISTACHE",
                "/store-assets/acai/pistache.jpg",
                isFeatured: true,
                isNew: true,
                discountAmount: 0m,
                [
                    ("300 ml", 23.90m, 9.80m, 300m, 3, "2 camadas gourmet"),
                    ("500 ml", 30.90m, 13.20m, 500m, 4, "3 camadas gourmet"),
                    ("700 ml", 36.90m, 16.15m, 700m, 5, "3 camadas super")
                ]),

            CreateAcaiItem(
                catPremium.Id,
                "Nutella",
                23.90m,
                "Açaí cremoso com Nutella original na borda e topo, morangos frescos, pedaços de Bis e leite em pó.",
                "ACAI-NUTELLA",
                "/store-assets/acai/nutella.jpg",
                isFeatured: true,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 23.90m, 10.10m, 300m, 3, "2 camadas gourmet"),
                    ("500 ml", 30.90m, 13.70m, 500m, 4, "3 camadas gourmet"),
                    ("700 ml", 38.90m, 17.50m, 700m, 5, "3 camadas super")
                ]),

            CreateAcaiItem(
                catPremium.Id,
                "Oreo Supreme",
                22.90m,
                "Açaí cremoso intercalado com creme especial de Oreo, bolacha Oreo triturada, leite em pó e brigadeiro.",
                "ACAI-OREO",
                "/store-assets/acai/oreo-supreme.jpg",
                isFeatured: false,
                isNew: true,
                discountAmount: 0m,
                [
                    ("300 ml", 22.90m, 9.20m, 300m, 3, "2 camadas"),
                    ("500 ml", 28.90m, 12.60m, 500m, 4, "3 camadas"),
                    ("700 ml", 35.90m, 15.90m, 700m, 5, "3 camadas")
                ]),

            CreateAcaiItem(
                catPremium.Id,
                "Par Perfeito",
                24.90m,
                "Açaí com morangos frescos, Bis branco e preto, creme de Ninho, creme de avelã, leite em pó e 2 corações de chocolate.",
                "ACAI-PAR-PERF",
                "/store-assets/acai/par-perfeito.jpg",
                isFeatured: false,
                isNew: true,
                discountAmount: 0m,
                [
                    ("300 ml", 24.90m, 10.80m, 300m, 3, "Edição especial"),
                    ("500 ml", 31.90m, 14.30m, 500m, 4, "Edição especial"),
                    ("700 ml", 38.90m, 17.80m, 700m, 5, "Edição especial")
                ]),

            CreateAcaiItem(
                catPremium.Id,
                "Doce Tentação",
                22.90m,
                "Açaí cremoso com creme de avelã, bombom Sonho de Valsa picado, morangos frescos e Leite Ninho em pó.",
                "ACAI-DOCE-TENT",
                "/store-assets/acai/doce-tentacao.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 2.00m,
                [
                    ("300 ml", 22.90m, 9.60m, 300m, 3, "2 camadas"),
                    ("500 ml", 29.90m, 13.10m, 500m, 4, "3 camadas"),
                    ("700 ml", 37.90m, 16.90m, 700m, 5, "3 camadas")
                ]),

            // 4. Explosão Kids
            CreateAcaiItem(
                catKids.Id,
                "Kids Fini",
                15.90m,
                "Açaí tradicional cremoso intercalado com chocobol crocante, mix Fini picado e cobertura sabor Fini.",
                "ACAI-KIDS-FINI",
                "/store-assets/acai/kids-fini.jpg",
                isFeatured: true,
                isNew: false,
                discountAmount: 0m,
                [("250 ml", 15.90m, 7.10m, 250m, 3, "Copo Kids 250 ml")]),

            CreateAcaiItem(
                catKids.Id,
                "Kids Moranguinho",
                15.90m,
                "Açaí tradicional cremoso em camadas com creme de Ninho, morangos frescos fatiados e confetes coloridos.",
                "ACAI-KIDS-MOR",
                "/store-assets/acai/kids-moranguinho.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("250 ml", 15.90m, 7.59m, 250m, 3, "Copo Kids 250 ml")]),

            CreateAcaiItem(
                catKids.Id,
                "Kids Choco",
                15.90m,
                "Açaí tradicional cremoso com brigadeiro, chocolate granulado e Bis picadinho.",
                "ACAI-KIDS-CHOCO",
                "/store-assets/acai/kids-choco.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("250 ml", 15.90m, 7.35m, 250m, 3, "Copo Kids 250 ml")]),

            // 5. Linha Leve & Fitness
            CreateAcaiItem(
                catFitness.Id,
                "Açaí Fitness Tropical",
                22.90m,
                "Açaí zero açúcar leve com banana fatiada, morangos frescos, sementes de chia e mel para decoração.",
                "ACAI-FIT-TROP",
                "/store-assets/acai/fitness-tropical.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 22.90m, 10.90m, 300m, 3, "Zero açúcar"),
                    ("500 ml", 30.90m, 15.40m, 500m, 4, "Zero açúcar")
                ]),

            CreateAcaiItem(
                catFitness.Id,
                "Açaí Fitness Power",
                26.90m,
                "Açaí zero açúcar, dose de Whey Protein, rodelas de banana, morangos frescos, sementes de chia e pasta de amendoim sem açúcar.",
                "ACAI-FIT-PWR",
                "/store-assets/acai/fitness-power.jpg",
                isFeatured: false,
                isNew: true,
                discountAmount: 0m,
                [
                    ("300 ml", 26.90m, 14.20m, 300m, 3, "Com Whey Protein"),
                    ("500 ml", 35.90m, 19.80m, 500m, 4, "Dose reforçada de Whey")
                ]),

            // 6. Explosão Natural
            CreateAcaiItem(
                catNatural.Id,
                "100% Açaí",
                16.90m,
                "Açaí pura polpa, cremoso e encorpado, sem acompanhamentos. Ideal para quem aprecia o sabor original.",
                "ACAI-100",
                "/store-assets/acai/puro-100.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 16.90m, 6.20m, 300m, 2, "Pura polpa"),
                    ("500 ml", 22.90m, 9.10m, 500m, 3, "Pura polpa"),
                    ("700 ml", 28.90m, 12.30m, 700m, 4, "Pura polpa")
                ]),

            CreateAcaiItem(
                catNatural.Id,
                "Açaí Mix-Fruta",
                20.90m,
                "Mix completo de frutas frescas no açaí: manga, morango, banana, uva sem caroço, kiwi e leite condensado.",
                "ACAI-MIX-FRUTA",
                "/store-assets/acai/mix-fruta.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [
                    ("300 ml", 20.90m, 8.90m, 300m, 3, "Mix de frutas frescas"),
                    ("500 ml", 26.90m, 12.10m, 500m, 4, "Mix de frutas frescas"),
                    ("700 ml", 33.90m, 15.80m, 700m, 5, "Mix de frutas frescas")
                ]),

            // 7. Açaí na Garrafa (300 ml)
            CreateAcaiItem(
                catGarrafa.Id,
                "Açaí Tradicional na Garrafa",
                16.90m,
                "Açaí tradicional cremoso batido com leite condensado, pronto para abrir e aproveitar a qualquer hora.",
                "GAR-TRAD-300",
                "/store-assets/acai/garrafa-tradicional.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("300 ml", 16.90m, 6.10m, 300m, 0, "Garrafa 300 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catGarrafa.Id,
                "Açaí na Garrafa – Morango",
                18.90m,
                "Açaí cremoso batido com creme de morango natural e polpa da fruta na garrafa de 300 ml.",
                "GAR-MOR-300",
                "/store-assets/acai/garrafa-morango.jpg",
                isFeatured: true,
                isNew: true,
                discountAmount: 0m,
                [("300 ml", 18.90m, 7.20m, 300m, 0, "Garrafa 300 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catGarrafa.Id,
                "Açaí na Garrafa – Maracujá",
                17.90m,
                "Açaí cremoso batido com mousse de maracujá natural e polpa da fruta na garrafa de 300 ml.",
                "GAR-MAR-300",
                "/store-assets/acai/garrafa-maracuja.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("300 ml", 17.90m, 6.80m, 300m, 0, "Garrafa 300 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catGarrafa.Id,
                "Açaí na Garrafa – Paçoca",
                17.90m,
                "Açaí cremoso com creme de amendoim e paçoca na garrafa de 300 ml.",
                "GAR-PAC-300",
                "/store-assets/acai/garrafa-pacoca.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("300 ml", 17.90m, 6.70m, 300m, 0, "Garrafa 300 ml")],
                attachFreeToppings: false),

            // 8. Explosão Família (Potes)
            CreateAcaiItem(
                catFamilia.Id,
                "Açaí Puro com Açúcar (1 Litro)",
                36.90m,
                "Pote família de 1 litro (900 g) de açaí tradicional puro com açúcar, cremoso e pronto para servir em casa.",
                "POTE-1L",
                "/store-assets/acai/puro-100.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("1 Litro", 36.90m, 16.50m, 1000m, 0, "Pote 1 Litro (900g)")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catFamilia.Id,
                "Açaí Puro com Açúcar (2 Litros)",
                64.90m,
                "Pote econômico família de 2 litros (1.800 g) de açaí tradicional puro com açúcar, ideal para compartilhar.",
                "POTE-2L",
                "/store-assets/acai/puro-100.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 5.00m,
                [("2 Litros", 64.90m, 32.00m, 2000m, 0, "Pote 2 Litros (1,8kg)")],
                attachFreeToppings: false),

            // 9. Mousses & Sobremesas (110 ml)
            CreateAcaiItem(
                catMousses.Id,
                "Mousse Ninho Trufado",
                9.90m,
                "Mousse cremoso de Leite Ninho finalizado com camada generosa de Ninho em pó no pote de 110 ml.",
                "MOUSSE-NINHO",
                "/store-assets/acai/mousse-tradicional.png",
                isFeatured: true,
                isNew: false,
                discountAmount: 0m,
                [("110 ml", 9.90m, 4.90m, 110m, 0, "Pote individual 110 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catMousses.Id,
                "Mousse de Morango",
                8.90m,
                "Mousse cremoso de morango servido no pote individual de 110 ml. Ótimo como sobremesa ou acompanhamento.",
                "MOUSSE-MOR",
                "/store-assets/acai/mousse-morango.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 1.00m,
                [("110 ml", 8.90m, 5.10m, 110m, 0, "Pote individual 110 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catMousses.Id,
                "Mousse de Maracujá",
                7.90m,
                "Mousse cremoso de maracujá servido no pote individual de 110 ml.",
                "MOUSSE-MAR",
                "/store-assets/acai/mousse-maracuja.jpg",
                isFeatured: false,
                isNew: true,
                discountAmount: 0m,
                [("110 ml", 7.90m, 4.50m, 110m, 0, "Pote individual 110 ml")],
                attachFreeToppings: false),

            CreateAcaiItem(
                catMousses.Id,
                "Mousse de Paçoca",
                7.90m,
                "Mousse cremoso de paçoca servido no pote individual de 110 ml.",
                "MOUSSE-PAC",
                "/store-assets/acai/mousse-pacoca.jpg",
                isFeatured: false,
                isNew: false,
                discountAmount: 0m,
                [("110 ml", 7.90m, 4.10m, 110m, 0, "Pote individual 110 ml")],
                attachFreeToppings: false)
        };

        foreach (var p in products)
        {
            await _productRepo.AddAsync(p, ct);
        }
        await _categoryRepo.SaveChangesAsync(ct);

        return Result.Success(new AppliedSegmentTemplateSummary("acaiteria", 9, products.Count, 5));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyPizzariaAsync(Guid tenantId, CancellationToken ct)
    {
        var catTrad = Category.Create(tenantId, "Pizzas Artesanais", "Escolha o tamanho, até 3 sabores e borda recheada", null, 1, "pizzaria").Value;
        await _categoryRepo.AddAsync(catTrad, ct);

        var grpSabores = CreateGroup(tenantId, "Escolha os Sabores", ModifierGroupType.Complement, 1, 2, true, 1, ModifierGroupKind.FlavorSelection, 0, OptionCountingMode.ByDistinctOption, "sabores", [
            ("Calabresa Artesanal", 49.90m, 1, "Molho de tomate italiano, mussarela e calabresa fatiada"),
            ("Marguerita Especial", 47.90m, 1, "Mussarela de búfala, manjericão fresco e tomate"),
            ("Frango com Catupiry Original", 54.90m, 1, "Peito de frango desfiado com requeijão cremoso"),
            ("Quatro Queijos", 56.90m, 1, "Mussarela, provolone, parmesão e gorgonzola"),
            ("Camarão Gourmet", 69.90m, 1, "Camarões salteados no alho e ervas")
        ]);

        var grpBordas = CreateGroup(tenantId, "Borda Recheada", ModifierGroupType.Addon, 0, 1, false, 2, ModifierGroupKind.RequiredChoice, 0, OptionCountingMode.ByDistinctOption, "bordas", [
            ("Borda Tradicional", 0m, 1, "Massa assada sem recheio"),
            ("Borda de Catupiry", 10.00m, 1, "Recheio cremoso na borda"),
            ("Borda de Cheddar Cremoso", 10.00m, 1, null),
            ("Borda Vulcão Chocolate", 14.00m, 1, null)
        ]);

        var grpExtras = CreateGroup(tenantId, "Adicionais Extras", ModifierGroupType.Addon, 0, 5, false, 3, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Bacon Crocante Extra", 8.00m, 2, null),
            ("Mussarela Extra", 9.00m, 2, null),
            ("Rúcula com Tomate Seco", 7.00m, 1, null)
        ]);

        var grpRemover = CreateGroup(tenantId, "Remover Ingredientes", ModifierGroupType.Complement, 0, 4, false, 4, ModifierGroupKind.IngredientRemoval, 4, OptionCountingMode.ByDistinctOption, "remocoes", [
            ("Sem Cebola", 0m, 1, null),
            ("Sem Orégano", 0m, 1, null),
            ("Sem Azeitona", 0m, 1, null)
        ]);

        await _modifierRepo.AddAsync(grpSabores, ct);
        await _modifierRepo.AddAsync(grpBordas, ct);
        await _modifierRepo.AddAsync(grpExtras, ct);
        await _modifierRepo.AddAsync(grpRemover, ct);

        var pizza = Product.Create(
            tenantId,
            catTrad.Id,
            "Pizza Personalizada (Sabores)",
            Money.Create(47.90m).Value,
            "Monte sua pizza inteira, meio a meio ou 3 sabores (preço pelo maior sabor).",
            "PIZZA-CUSTOM",
            ProductSaleMode.Unit,
            isFeatured: true,
            displayOrder: 1,
            isNew: false,
            isRecommended: true,
            pricingStrategy: ProductPricingStrategy.FlavorHighestPrice,
            tagsCsv: "pizzaria,meia-a-meia",
            preparationMinutes: 30,
            customTemplateKey: "pizzaria-sabores").Value;

        var vBroto = pizza.AddVariation("Broto (4 fatias)", Money.Create(34.90m).Value, "PZ-BROTO", 1, "Até 1 sabor · 4 fatias", 4m, "fatias", 1).Value;
        var vMedia = pizza.AddVariation("Média (6 fatias)", Money.Create(47.90m).Value, "PZ-MEDIA", 2, "Até 2 sabores · 6 fatias", 6m, "fatias", 2).Value;
        var vGrande = pizza.AddVariation("Grande (8 fatias)", Money.Create(54.90m).Value, "PZ-GRANDE", 3, "Até 2 sabores · 8 fatias", 8m, "fatias", 2).Value;
        var vGigante = pizza.AddVariation("Gigante (12 fatias)", Money.Create(69.90m).Value, "PZ-GIG", 4, "Até 3 sabores · 12 fatias", 12m, "fatias", 3).Value;

        pizza.AttachModifierGroup(grpSabores.Id, 1);
        pizza.AttachModifierGroup(grpBordas.Id, 2);
        pizza.AttachModifierGroup(grpExtras.Id, 3);
        pizza.AttachModifierGroup(grpRemover.Id, 4);

        pizza.SetVariationRule(vBroto.Id, grpSabores.Id, 1, 1, 0, true);
        pizza.SetVariationRule(vMedia.Id, grpSabores.Id, 1, 2, 0, true);
        pizza.SetVariationRule(vGrande.Id, grpSabores.Id, 1, 2, 0, true);
        pizza.SetVariationRule(vGigante.Id, grpSabores.Id, 1, 3, 0, true);

        await _productRepo.AddAsync(pizza, ct);
        await _categoryRepo.SaveChangesAsync(ct);

        return Result.Success(new AppliedSegmentTemplateSummary("pizzaria", 1, 1, 4));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyHamburgueriaAsync(Guid tenantId, CancellationToken ct)
    {
        var catBurgers = Category.Create(tenantId, "Burgers Artesanais", "Escolha pão, carne, ponto, queijos, saladas e acompanhamentos", null, 1, "hamburgueria").Value;
        await _categoryRepo.AddAsync(catBurgers, ct);

        var grpPao = CreateGroup(tenantId, "Escolha do Pão", ModifierGroupType.Complement, 1, 1, true, 1, ModifierGroupKind.RequiredChoice, 1, OptionCountingMode.ByDistinctOption, "paes", [
            ("Pão Brioche Selado na Manteiga", 0m, 1, null),
            ("Pão Australiano", 2.00m, 1, null),
            ("Pão Integral com Gergelim", 1.50m, 1, null)
        ]);
        var grpCarne = CreateGroup(tenantId, "Tipo e Quantidade de Carne", ModifierGroupType.Addon, 1, 3, true, 2, ModifierGroupKind.RequiredChoice, 1, OptionCountingMode.ByTotalQuantity, "carnes", [
            ("Blend Bovino 160g", 0m, 3, "Adicional de 160g extra além da base"),
            ("Smash Crispy 90g", 0m, 3, null),
            ("Burger Vegetal Futuro", 3.00m, 2, null)
        ]);
        var grpPonto = CreateGroup(tenantId, "Ponto da Carne", ModifierGroupType.Complement, 1, 1, true, 3, ModifierGroupKind.RequiredChoice, 1, OptionCountingMode.ByDistinctOption, "ponto", [
            ("Ao Ponto (Suculento)", 0m, 1, null),
            ("Mal Passado", 0m, 1, null),
            ("Bem Passado", 0m, 1, null)
        ]);
        var grpQueijos = CreateGroup(tenantId, "Queijos e Adicionais", ModifierGroupType.Addon, 0, 5, false, 4, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "queijos", [
            ("Cheddar Inglês", 4.50m, 2, null),
            ("Mussarela Gratinada", 4.00m, 2, null),
            ("Bacon Caramelizado", 6.00m, 2, null),
            ("Cebola Crispy", 4.00m, 1, null)
        ]);
        var grpMolhos = CreateGroupWithReplaces(tenantId, "Saladas, Molhos e Substituições", ModifierGroupType.Complement, 0, 4, false, 5, ModifierGroupKind.Substitution, 0, OptionCountingMode.ByDistinctOption, "molhos", [
            ("Maionese Verde da Casa", 0m, 1, null, null),
            ("Molho Barbecue Defumado", 0m, 1, null, null),
            ("Alface e Tomate Frescos", 0m, 1, null, null),
            ("Trocar Batata Frita por Onion Rings", 3.50m, 1, "Substituição de acompanhamento", "Batata Frita")
        ]);

        await _modifierRepo.AddAsync(grpPao, ct);
        await _modifierRepo.AddAsync(grpCarne, ct);
        await _modifierRepo.AddAsync(grpPonto, ct);
        await _modifierRepo.AddAsync(grpQueijos, ct);
        await _modifierRepo.AddAsync(grpMolhos, ct);

        var burger = Product.Create(
            tenantId,
            catBurgers.Id,
            "Craft Burger da Casa",
            Money.Create(28.90m).Value,
            "Hambúrguer artesanal montado na hora com escolha de pão, carnes, ponto, queijos e molhos.",
            "BURG-CRAFT",
            ProductSaleMode.Unit,
            true,
            1,
            true,
            true,
            ProductPricingStrategy.FixedPlusAddons,
            "hamburgueria,artesanal",
            18,
            "hamburgueria-burger").Value;
        burger.AddVariation("Simples (Só o Burger)", Money.Create(28.90m).Value, "BURG-S", 1);
        burger.AddVariation("Combo (Burger + Fritas + Bebida)", Money.Create(42.90m).Value, "BURG-C", 2);

        burger.AttachModifierGroup(grpPao.Id, 1);
        burger.AttachModifierGroup(grpCarne.Id, 2);
        burger.AttachModifierGroup(grpPonto.Id, 3);
        burger.AttachModifierGroup(grpQueijos.Id, 4);
        burger.AttachModifierGroup(grpMolhos.Id, 5);

        await _productRepo.AddAsync(burger, ct);
        await _categoryRepo.SaveChangesAsync(ct);

        return Result.Success(new AppliedSegmentTemplateSummary("hamburgueria", 1, 1, 5));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyPastelariaAsync(Guid tenantId, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Pastéis Feitos na Hora", "Massa crocante frita na hora com recheios generosos", null, 1, "pastelaria").Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpRecheio = CreateGroup(tenantId, "Escolha os Recheios", ModifierGroupType.Complement, 1, 3, true, 1, ModifierGroupKind.FreeIncluded, 2, OptionCountingMode.ByDistinctOption, "recheios", [
            ("Carne Moída Temperada", 0m, 1, null),
            ("Queijo Mussarela", 0m, 1, null),
            ("Frango Desfiado com Catupiry", 2.00m, 1, null),
            ("Calabresa Acebolada", 0m, 1, null),
            ("Palmito Cremoso", 2.50m, 1, null)
        ]);
        var grpExtras = CreateGroup(tenantId, "Adicionais Extras", ModifierGroupType.Addon, 0, 4, false, 2, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Catupiry Extra", 4.50m, 2, null),
            ("Bacon em Cubos", 5.00m, 2, null),
            ("Ovo Cozido", 2.50m, 1, null)
        ]);
        var grpRem = CreateGroup(tenantId, "Remover / Preferências", ModifierGroupType.Complement, 0, 3, false, 3, ModifierGroupKind.IngredientRemoval, 3, OptionCountingMode.ByDistinctOption, "remocoes", [
            ("Sem Cebola", 0m, 1, null),
            ("Sem Azeitona", 0m, 1, null),
            ("Sem Vinagrete à Parte", 0m, 1, null)
        ]);

        await _modifierRepo.AddAsync(grpRecheio, ct);
        await _modifierRepo.AddAsync(grpExtras, ct);
        await _modifierRepo.AddAsync(grpRem, ct);

        var pastel = Product.Create(tenantId, cat.Id, "Pastel Especial Montado", Money.Create(14.90m).Value, "Escolha tamanho e até 3 combinações de recheio.", "PAST-ESP", ProductSaleMode.Unit, true, 1, true, true, ProductPricingStrategy.FixedPlusAddons, "pastelaria", 10, "pastelaria").Value;
        var vMed = pastel.AddVariation("Médio (18cm)", Money.Create(14.90m).Value, "PAST-M", 1, "Até 2 recheios inclusos", 18m, "cm").Value;
        var vGrd = pastel.AddVariation("Grande (25cm)", Money.Create(21.90m).Value, "PAST-G", 2, "Até 3 recheios inclusos", 25m, "cm").Value;
        pastel.AttachModifierGroup(grpRecheio.Id, 1);
        pastel.AttachModifierGroup(grpExtras.Id, 2);
        pastel.AttachModifierGroup(grpRem.Id, 3);
        pastel.SetVariationRule(vMed.Id, grpRecheio.Id, 1, 2, 2, true);
        pastel.SetVariationRule(vGrd.Id, grpRecheio.Id, 1, 3, 3, true);

        await _productRepo.AddAsync(pastel, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary("pastelaria", 1, 1, 3));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplySorveteriaAsync(Guid tenantId, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Sorvetes Artesanais", "Casquinhas, copinhos por quantidade de bolas e potes", null, 1, "sorveteria").Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpSabores = CreateGroup(tenantId, "Sabores de Sorvete", ModifierGroupType.Complement, 1, 3, true, 1, ModifierGroupKind.FlavorSelection, 3, OptionCountingMode.ByTotalQuantity, "sabores", [
            ("Chocolate Belga", 0m, 3, null),
            ("Morango ao Leite", 0m, 3, null),
            ("Ninho Trufado", 0m, 3, null),
            ("Flocos Tradicional", 0m, 3, null),
            ("Pistache Italiano", 3.50m, 3, null)
        ]);
        var grpCoberturas = CreateGroup(tenantId, "Coberturas e Complementos Grátis", ModifierGroupType.Complement, 0, 3, false, 2, ModifierGroupKind.FreeIncluded, 2, OptionCountingMode.ByDistinctOption, "coberturas", [
            ("Calda de Chocolate", 0m, 1, null),
            ("Calda de Caramelo", 0m, 1, null),
            ("Granulado Crocante", 0m, 1, null),
            ("Bijou / Tubete", 0m, 1, null)
        ]);
        var grpFrutasExtras = CreateGroup(tenantId, "Frutas e Adicionais Pagos", ModifierGroupType.Addon, 0, 4, false, 3, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Morango Picado", 4.00m, 2, null),
            ("Nutella", 6.50m, 2, null),
            ("Chantilly", 3.50m, 1, null)
        ]);

        await _modifierRepo.AddAsync(grpSabores, ct);
        await _modifierRepo.AddAsync(grpCoberturas, ct);
        await _modifierRepo.AddAsync(grpFrutasExtras, ct);

        var sorvete = Product.Create(tenantId, cat.Id, "Sorvete Artesanal Personalizado", Money.Create(11.90m).Value, "Escolha o tamanho/bolas, sabores, coberturas e adicionais.", "SORV-01", ProductSaleMode.Unit, true, 1, false, true, ProductPricingStrategy.FixedPlusAddons, "sorveteria", 5, "sorveteria").Value;
        var v1 = sorvete.AddVariation("Casquinha 1 Bola", Money.Create(11.90m).Value, "SORV-1B", 1, "1 bola + até 2 coberturas grátis", 1m, "bolas", 1).Value;
        var v2 = sorvete.AddVariation("Copinho 2 Bolas", Money.Create(17.90m).Value, "SORV-2B", 2, "2 bolas + até 2 coberturas grátis", 2m, "bolas", 2).Value;
        var v3 = sorvete.AddVariation("Copinho 3 Bolas", Money.Create(23.90m).Value, "SORV-3B", 3, "3 bolas + até 3 coberturas grátis", 3m, "bolas", 3).Value;

        sorvete.AttachModifierGroup(grpSabores.Id, 1);
        sorvete.AttachModifierGroup(grpCoberturas.Id, 2);
        sorvete.AttachModifierGroup(grpFrutasExtras.Id, 3);
        sorvete.SetVariationRule(v1.Id, grpSabores.Id, 1, 1, 1, true);
        sorvete.SetVariationRule(v2.Id, grpSabores.Id, 1, 2, 2, true);
        sorvete.SetVariationRule(v3.Id, grpSabores.Id, 1, 3, 3, true);
        sorvete.SetVariationRule(v3.Id, grpCoberturas.Id, 0, 3, 3, false);

        await _productRepo.AddAsync(sorvete, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary("sorveteria", 1, 1, 3));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyRestauranteLanchoneteAsync(Guid tenantId, string code, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Pratos & Refeições", "Escolha o tamanho, proteína, acompanhamentos e molhos", null, 1, code).Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpProteina = CreateGroup(tenantId, "Proteína Principal", ModifierGroupType.Complement, 1, 1, true, 1, ModifierGroupKind.RequiredChoice, 1, OptionCountingMode.ByDistinctOption, "proteinas", [
            ("Filé de Frango Grelhado", 0m, 1, null),
            ("Contra-Filé na Chapa", 6.00m, 1, null),
            ("Salmão Grelhado", 14.00m, 1, null)
        ]);
        var grpAcomp = CreateGroup(tenantId, "Acompanhamentos do Prato", ModifierGroupType.Complement, 1, 4, true, 2, ModifierGroupKind.FreeIncluded, 3, OptionCountingMode.ByDistinctOption, "acompanhamentos", [
            ("Arroz Branco Soltinho", 0m, 1, null),
            ("Feijão Carioca Caseiro", 0m, 1, null),
            ("Batata Frita Crocante", 0m, 1, null),
            ("Purê de Batatas", 0m, 1, null),
            ("Salada Mix Fresca", 0m, 1, null)
        ]);
        var grpSubs = CreateGroupWithReplaces(tenantId, "Substituições e Extras", ModifierGroupType.Addon, 0, 4, false, 3, ModifierGroupKind.Substitution, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Trocar Arroz por Arroz Integral", 2.00m, 1, null, "Arroz Branco"),
            ("Ovo Frito Extra", 3.50m, 2, null, null),
            ("Molho Madeira / Ervas", 4.00m, 1, null, null)
        ]);

        await _modifierRepo.AddAsync(grpProteina, ct);
        await _modifierRepo.AddAsync(grpAcomp, ct);
        await _modifierRepo.AddAsync(grpSubs, ct);

        var prato = Product.Create(tenantId, cat.Id, "Prato Executivo Montado", Money.Create(29.90m).Value, "Monte sua refeição escolhendo porção, proteína e acompanhamentos.", "EXEC-01", ProductSaleMode.Unit, true, 1, false, true, ProductPricingStrategy.FixedPlusAddons, $"{code},executivo", 20, "restaurante").Value;
        var vP = prato.AddVariation("Porção P (Individual)", Money.Create(24.90m).Value, "EXEC-P", 1, "Até 2 acompanhamentos", 350m, "g").Value;
        var vM = prato.AddVariation("Porção M (Executiva)", Money.Create(29.90m).Value, "EXEC-M", 2, "Até 3 acompanhamentos", 500m, "g").Value;
        var vG = prato.AddVariation("Porção G (Super)", Money.Create(36.90m).Value, "EXEC-G", 3, "Até 4 acompanhamentos", 700m, "g").Value;

        prato.AttachModifierGroup(grpProteina.Id, 1);
        prato.AttachModifierGroup(grpAcomp.Id, 2);
        prato.AttachModifierGroup(grpSubs.Id, 3);
        prato.SetVariationRule(vP.Id, grpAcomp.Id, 1, 2, 2, true);
        prato.SetVariationRule(vM.Id, grpAcomp.Id, 1, 3, 3, true);
        prato.SetVariationRule(vG.Id, grpAcomp.Id, 1, 4, 4, true);

        await _productRepo.AddAsync(prato, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary(code, 1, 1, 3));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyAdegaAsync(Guid tenantId, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Copões 700ml & Drinks", "Monte seu copão escolhendo destilado, energético e gelo de sabor", null, 1, "adega").Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpGelo = CreateGroup(tenantId, "Gelo de Sabor", ModifierGroupType.Complement, 1, 2, true, 1, ModifierGroupKind.FreeIncluded, 1, OptionCountingMode.ByDistinctOption, "gelo", [
            ("Gelo de Coco", 0m, 1, null),
            ("Gelo de Maracujá", 0m, 1, null),
            ("Gelo de Melancia", 0m, 1, null)
        ]);
        var grpExtra = CreateGroup(tenantId, "Dose Extra / Energético", ModifierGroupType.Addon, 0, 3, false, 2, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Dose Extra Whisky 50ml", 7.00m, 2, null),
            ("Lata Red Bull 250ml", 11.00m, 2, null)
        ]);

        await _modifierRepo.AddAsync(grpGelo, ct);
        await _modifierRepo.AddAsync(grpExtra, ct);

        var copao = Product.Create(tenantId, cat.Id, "Copão Whisky + Energético + Gelo", Money.Create(22.90m).Value, "Copão 700ml montado na hora com dose generosa e energético gelado.", "ADEGA-COPAO", ProductSaleMode.Unit, true, 1, false, true, ProductPricingStrategy.FixedPlusAddons, "adega,drinks", 5, "adega").Value;
        copao.AddVariation("Copão Red Label + Baly 700ml", Money.Create(22.90m).Value, "ADEGA-RL", 1, "80ml Red Label + Baly", 700m, "ml");
        copao.AddVariation("Copão Jack Daniel's + Red Bull 700ml", Money.Create(32.90m).Value, "ADEGA-JD", 2, "80ml Jack Daniel's + Red Bull", 700m, "ml");
        copao.AttachModifierGroup(grpGelo.Id, 1);
        copao.AttachModifierGroup(grpExtra.Id, 2);

        await _productRepo.AddAsync(copao, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary("adega", 1, 1, 2));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyPadariaAsync(Guid tenantId, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Padaria & Lanches na Chapa", "Pães quentinhos, salgados e cafés para viagem", null, 1, "padaria").Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpExtras = CreateGroup(tenantId, "Adicionais na Chapa", ModifierGroupType.Addon, 0, 3, false, 1, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Requeijão Cremoso Extra", 3.00m, 2, null),
            ("Muçarela Extra", 4.00m, 2, null),
            ("Ovo na Chapa", 2.00m, 2, null)
        ]);
        await _modifierRepo.AddAsync(grpExtras, ct);

        var misto = Product.Create(tenantId, cat.Id, "Misto Quente Caprichado", Money.Create(11.90m).Value, "Pão tostado na manteiga com presunto e muçarela derretida.", "PAD-MISTO", ProductSaleMode.Unit, true, 1, false, true, ProductPricingStrategy.FixedPlusAddons, "padaria", 10, "padaria").Value;
        misto.AttachModifierGroup(grpExtras.Id, 1);
        await _productRepo.AddAsync(misto, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary("padaria", 1, 1, 1));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyDoceriaAsync(Guid tenantId, CancellationToken ct)
    {
        var cat = Category.Create(tenantId, "Doces, Copos da Felicidade & Bolos", "Sobremesas artesanais preparadas com chocolate nobre", null, 1, "doceria").Value;
        await _categoryRepo.AddAsync(cat, ct);

        var grpExtras = CreateGroup(tenantId, "Coberturas & Extras Gourmet", ModifierGroupType.Addon, 0, 4, false, 1, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "extras", [
            ("Creme de Nutella Original", 5.00m, 2, null),
            ("Morango Fresco Picado", 4.00m, 2, null),
            ("Chantilly Profissional", 3.00m, 1, null)
        ]);
        await _modifierRepo.AddAsync(grpExtras, ct);

        var copo = Product.Create(tenantId, cat.Id, "Copo da Felicidade Kinder / Nutella", Money.Create(21.90m).Value, "Camadas de creme de Ninho, Nutella pura, brownie, morangos e chantilly.", "DOCE-COPO", ProductSaleMode.Unit, true, 1, true, true, ProductPricingStrategy.FixedPlusAddons, "doceria", 10, "doceria").Value;
        copo.AddVariation("300 ml", Money.Create(21.90m).Value, "DOCE-300", 1, "300 ml", 300m, "ml");
        copo.AttachModifierGroup(grpExtras.Id, 1);
        await _productRepo.AddAsync(copo, ct);
        await _categoryRepo.SaveChangesAsync(ct);
        return Result.Success(new AppliedSegmentTemplateSummary("doceria", 1, 1, 1));
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyGenericFoodAsync(Guid tenantId, string code, CancellationToken ct) =>
        await ApplyRestauranteLanchoneteAsync(tenantId, code, ct);

    private static ModifierGroup CreateGroup(
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder,
        ModifierGroupKind kind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string categoryTag,
        (string Name, decimal Price, int MaxQty, string? Desc)[] options) =>
        CreateGroupWithReplaces(
            tenantId, name, type, minSelections, maxSelections, isRequired, displayOrder, kind, freeAllowance, countingMode, categoryTag,
            options.Select(o => (o.Name, o.Price, o.MaxQty, o.Desc, (string?)null)).ToArray());

    private static ModifierGroup CreateGroupWithCost(
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder,
        ModifierGroupKind kind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string categoryTag,
        (string Name, decimal Price, decimal CostPrice, int MaxQty, string? Desc)[] options)
    {
        var grp = ModifierGroup.Create(tenantId, name, type, minSelections, maxSelections, isRequired, displayOrder, kind, freeAllowance, countingMode, categoryTag).Value;
        var order = 0;
        foreach (var opt in options)
        {
            grp.AddOption(opt.Name, Money.Create(opt.Price).Value, opt.MaxQty, order++, opt.Desc, null, 0m, null, 0m, null, null, Money.Create(opt.CostPrice).Value);
        }
        return grp;
    }

    private static ModifierGroup CreateGroupWithReplaces(
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder,
        ModifierGroupKind kind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string categoryTag,
        (string Name, decimal Price, int MaxQty, string? Desc, string? Replaces)[] options)
    {
        var grp = ModifierGroup.Create(tenantId, name, type, minSelections, maxSelections, isRequired, displayOrder, kind, freeAllowance, countingMode, categoryTag).Value;
        var order = 0;
        foreach (var opt in options)
        {
            grp.AddOption(opt.Name, Money.Create(opt.Price).Value, opt.MaxQty, order++, opt.Desc, null, 0m, null, 0m, opt.Replaces, null);
        }
        return grp;
    }
}
