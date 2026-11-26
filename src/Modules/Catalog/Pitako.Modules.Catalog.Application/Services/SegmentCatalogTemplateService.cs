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

        var code = (segmentCode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<AppliedSegmentTemplateSummary>(Error.Validation("Catalog.SegmentRequired", "Segmento é obrigatório."));

        return code switch
        {
            "acaiteria" => await ApplyAcaiteriaAsync(tenantId, cancellationToken),
            "pizzaria" => await ApplyPizzariaAsync(tenantId, cancellationToken),
            "hamburgueria" => await ApplyHamburgueriaAsync(tenantId, cancellationToken),
            "pastelaria" => await ApplyPastelariaAsync(tenantId, cancellationToken),
            "sorveteria" => await ApplySorveteriaAsync(tenantId, cancellationToken),
            "restaurante" or "lanchonete" => await ApplyRestauranteLanchoneteAsync(tenantId, code, cancellationToken),
            _ => await ApplyGenericFoodAsync(tenantId, code, cancellationToken)
        };
    }

    private async Task<Result<AppliedSegmentTemplateSummary>> ApplyAcaiteriaAsync(Guid tenantId, CancellationToken ct)
    {
        var catMonte = Category.Create(tenantId, "Monte seu Açaí", "Escolha o tamanho do copo e monte com seus ingredientes favoritos", null, 1, "acaiteria", "Limites dinâmicos por tamanho configuráveis").Value;
        var catProntos = Category.Create(tenantId, "Açaís Prontos da Casa", "Combinações exclusivas prontas com receita padrão", null, 2, "acaiteria").Value;
        await _categoryRepo.AddAsync(catMonte, ct);
        await _categoryRepo.AddAsync(catProntos, ct);

        var grpBase = CreateGroup(tenantId, "Base e Creme Principal", ModifierGroupType.Complement, 1, 2, true, 1, ModifierGroupKind.RequiredChoice, 2, OptionCountingMode.ByDistinctOption, "cremes", [
            ("Açaí Tradicional Cremoso", 0m, 1, "Padrão artesanal"),
            ("Açaí Zero Açúcar", 2.00m, 1, "Batido sem xarope"),
            ("Creme de Cupuaçu", 0m, 1, "Base de fruta amazônica"),
            ("Creme de Ninho", 2.50m, 1, "Creme lácteo batido")
        ]);

        var grpGratis = CreateGroup(tenantId, "Acompanhamentos Inclusos", ModifierGroupType.Complement, 0, 7, false, 2, ModifierGroupKind.FreeIncluded, 3, OptionCountingMode.ByTotalQuantity, "complementos", [
            ("Leite em Pó Ninho", 0m, 2, "Porção de 30g"),
            ("Granola Crocante", 0m, 2, "Porção de 20g"),
            ("Paçoca Triturada", 0m, 2, "Porção de 20g"),
            ("Leite Condensado", 0m, 2, "Porção de 25ml"),
            ("Calda de Chocolate", 0m, 2, "Porção de 25ml"),
            ("Calda de Morango", 0m, 2, "Porção de 25ml"),
            ("Ovomaltine Flocos", 0m, 2, "Porção de 20g"),
            ("Amendoim Granulado", 0m, 2, "Porção de 20g"),
            ("Confete Colorido", 0m, 2, "Porção de 20g")
        ]);

        var grpFrutas = CreateGroup(tenantId, "Frutas Frescas e Cremes Especiais", ModifierGroupType.Addon, 0, 8, false, 3, ModifierGroupKind.PaidAddon, 0, OptionCountingMode.ByTotalQuantity, "frutas", [
            ("Morango Fresco Fatiado", 4.50m, 2, "Porção de 40g"),
            ("Banana Prata", 3.00m, 2, "Porção de 40g"),
            ("Kiwi Selecionado", 5.00m, 2, "Porção de 35g"),
            ("Creme de Nutella Original", 7.00m, 2, "Porção de 35g"),
            ("Creme de Pistache", 8.50m, 2, "Porção de 30g"),
            ("Gotas de Chocolate Meio Amargo", 4.00m, 2, "Porção de 25g")
        ]);

        var grpRemocao = CreateGroup(tenantId, "Preferências / Remover Ingredientes", ModifierGroupType.Complement, 0, 3, false, 4, ModifierGroupKind.IngredientRemoval, 3, OptionCountingMode.ByDistinctOption, "remocoes", [
            ("Sem Granola", 0m, 1, null),
            ("Sem Leite Condensado", 0m, 1, null),
            ("Sem Colher Descartável", 0m, 1, null)
        ]);

        await _modifierRepo.AddAsync(grpBase, ct);
        await _modifierRepo.AddAsync(grpGratis, ct);
        await _modifierRepo.AddAsync(grpFrutas, ct);
        await _modifierRepo.AddAsync(grpRemocao, ct);

        var prodMonte = Product.Create(
            tenantId,
            catMonte.Id,
            "Monte do Seu Jeito",
            Money.Create(16.90m).Value,
            "Monte seu copo ou tigela com açaí puro, acompanhamentos inclusos conforme o tamanho e extras.",
            "ACAI-MONTE",
            ProductSaleMode.Unit,
            isFeatured: true,
            displayOrder: 1,
            isNew: false,
            isRecommended: true,
            pricingStrategy: ProductPricingStrategy.FixedPlusAddons,
            tagsCsv: "acaiteria,monte,personalizavel",
            preparationMinutes: 8,
            customTemplateKey: "acaiteria-monte").Value;

        var v300 = prodMonte.AddVariation("Copo 300 ml", Money.Create(16.90m).Value, "ACAI-300", 1, "260g de açaí + até 3 grátis", 300m, "ml", 1).Value;
        var v500 = prodMonte.AddVariation("Copo 500 ml", Money.Create(22.90m).Value, "ACAI-500", 2, "420g de açaí + até 5 grátis", 500m, "ml", 1).Value;
        var v700 = prodMonte.AddVariation("Copo 700 ml", Money.Create(29.90m).Value, "ACAI-700", 3, "600g de açaí + até 7 grátis", 700m, "ml", 1).Value;
        var v1000 = prodMonte.AddVariation("Tigela 1 Litro", Money.Create(42.90m).Value, "ACAI-1L", 4, "900g de açaí + até 10 grátis", 1000m, "ml", 1).Value;

        prodMonte.AttachModifierGroup(grpBase.Id, 1);
        prodMonte.AttachModifierGroup(grpGratis.Id, 2);
        prodMonte.AttachModifierGroup(grpFrutas.Id, 3);
        prodMonte.AttachModifierGroup(grpRemocao.Id, 4);

        prodMonte.SetVariationRule(v300.Id, grpGratis.Id, 0, 3, 3, false);
        prodMonte.SetVariationRule(v500.Id, grpGratis.Id, 0, 5, 5, false);
        prodMonte.SetVariationRule(v700.Id, grpGratis.Id, 0, 7, 7, false);
        prodMonte.SetVariationRule(v1000.Id, grpGratis.Id, 0, 10, 10, false);

        prodMonte.SetVariationRule(v300.Id, grpFrutas.Id, 0, 4, 0, false);
        prodMonte.SetVariationRule(v500.Id, grpFrutas.Id, 0, 6, 0, false);
        prodMonte.SetVariationRule(v700.Id, grpFrutas.Id, 0, 8, 0, false);

        var prodPronto = Product.Create(
            tenantId,
            catProntos.Id,
            "Explosão Ninho com Morango",
            Money.Create(25.90m).Value,
            "Receita pronta da casa com açaí cremoso, morango fresco, leite em pó e creme de ninho.",
            "ACAI-NINHO-MOR",
            ProductSaleMode.Unit,
            isFeatured: true,
            displayOrder: 2,
            isNew: true,
            isRecommended: true,
            pricingStrategy: ProductPricingStrategy.FixedPlusAddons,
            tagsCsv: "acaiteria,pronto,novidade",
            preparationMinutes: 7).Value;
        prodPronto.AddVariation("Copo 500 ml", Money.Create(25.90m).Value, "PRONTO-500", 1, "Receita pronta 500ml", 500m, "ml");
        prodPronto.AttachModifierGroup(grpFrutas.Id, 1);
        prodPronto.AttachModifierGroup(grpRemocao.Id, 2);

        await _productRepo.AddAsync(prodMonte, ct);
        await _productRepo.AddAsync(prodPronto, ct);
        await _categoryRepo.SaveChangesAsync(ct);

        return Result.Success(new AppliedSegmentTemplateSummary("acaiteria", 2, 2, 4));
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
