using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class UniversalCustomizationEngineUnitTests
{
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class InMemoryCategoryRepo : ICategoryRepository
    {
        public readonly List<Category> Items = [];
        public Task<Category?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Category>> ListOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Category>>(Items.OrderBy(x => x.DisplayOrder).ToList());
        public Task AddAsync(Category category, CancellationToken ct = default) { Items.Add(category); return Task.CompletedTask; }
        public void Remove(Category category) => Items.Remove(category);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryProductRepo : IProductRepository
    {
        public readonly List<Product> Items = [];
        public Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Product>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(Items.Where(x => ids.Contains(x.Id)).ToList());
        public Task<IReadOnlyList<Product>> ListAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(Items.OrderBy(x => x.DisplayOrder).ToList());
        public Task AddAsync(Product product, CancellationToken ct = default) { Items.Add(product); return Task.CompletedTask; }
        public void Remove(Product product) => Items.Remove(product);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryModifierGroupRepo : IModifierGroupRepository
    {
        public readonly List<ModifierGroup> Items = [];
        public Task<ModifierGroup?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<ModifierGroup>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModifierGroup>>(Items.OrderBy(x => x.DisplayOrder).ToList());
        public Task AddAsync(ModifierGroup group, CancellationToken ct = default) { Items.Add(group); return Task.CompletedTask; }
        public void Remove(ModifierGroup group) => Items.Remove(group);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryComboRepo : IComboRepository
    {
        public readonly List<Combo> Items = [];
        public Task<Combo?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Combo>> ListAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Combo>>(Items.OrderBy(x => x.DisplayOrder).ToList());
        public Task AddAsync(Combo combo, CancellationToken ct = default) { Items.Add(combo); return Task.CompletedTask; }
        public void Remove(Combo combo) => Items.Remove(combo);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryPromoRepo : IPromotionRepository
    {
        public readonly List<Promotion> Items = [];
        public Task<Promotion?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Promotion>> ListActiveCandidatesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Promotion>>(Items.Where(p => p.IsActive).ToList());
        public Task<IReadOnlyList<Promotion>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Promotion>>(Items.ToList());
        public Task AddAsync(Promotion promo, CancellationToken ct = default) { Items.Add(promo); return Task.CompletedTask; }
        public void Remove(Promotion promo) => Items.Remove(promo);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoHistoryChecker : IProductHistoryReferenceChecker
    {
        public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeSupplyLookup(Dictionary<Guid, CatalogSupplyCostInfo> map) : ICatalogSupplyCostLookup
    {
        public Task<IReadOnlyDictionary<Guid, CatalogSupplyCostInfo>> GetSupplyCostsAsync(IEnumerable<Guid> supplyItemIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, CatalogSupplyCostInfo> res = supplyItemIds.Distinct()
                .Where(map.ContainsKey)
                .ToDictionary(id => id, id => map[id]);
            return Task.FromResult(res);
        }
    }

    private static (CatalogManagementService Svc, SegmentCatalogTemplateService Tpl, InMemoryProductRepo ProdRepo, InMemoryModifierGroupRepo ModRepo, Guid TenantId, User Admin) BuildHarness(Dictionary<Guid, CatalogSupplyCostInfo>? supplies = null)
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@teste.com", "Hash!123", EstablishmentRole.Administrator).Value;

        var catRepo = new InMemoryCategoryRepo();
        var prodRepo = new InMemoryProductRepo();
        var modRepo = new InMemoryModifierGroupRepo();
        var comboRepo = new InMemoryComboRepo();
        var promoRepo = new InMemoryPromoRepo();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero));
        var svc = new CatalogManagementService(catRepo, prodRepo, modRepo, comboRepo, promoRepo, new NoHistoryChecker(), ctx, clock, new FakeSupplyLookup(supplies ?? new()));
        var tpl = new SegmentCatalogTemplateService(catRepo, prodRepo, modRepo, ctx);
        return (svc, tpl, prodRepo, modRepo, tenantId, admin);
    }

    [Fact]
    public async Task Acaiteria_ShouldEnforceDynamicFreeAllowancePerCupVariation_AndComputeLossCost()
    {
        var granolaSupplyId = Guid.NewGuid();
        var nutellaSupplyId = Guid.NewGuid();
        var (svc, tpl, prodRepo, modRepo, _, admin) = BuildHarness(new Dictionary<Guid, CatalogSupplyCostInfo>
        {
            [granolaSupplyId] = new(granolaSupplyId, "Granola Crocante", "g", 0.04m, 5m),
            [nutellaSupplyId] = new(nutellaSupplyId, "Creme de Avelã", "g", 0.12m, 10m)
        });

        var applied = await tpl.ApplyTemplateAsync(admin, "acaiteria");
        Assert.True(applied.IsSuccess);

        var acai = prodRepo.Items.First(p => p.Name.Contains("Monte do Seu Jeito", StringComparison.OrdinalIgnoreCase));
        var v300 = acai.Variations.First(v => v.MeasureAmount == 300m);
        var v500 = acai.Variations.First(v => v.MeasureAmount == 500m);
        var baseGroup = modRepo.Items.First(g => g.IsRequired);
        var baseOpt = baseGroup.Options.First();
        var freeGroup = modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.FreeIncluded);
        var paidGroup = modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.PaidAddon);

        // Vincular insumo + perda 10% a uma opção grátis e a um adicional pago
        var g0 = freeGroup.Options.First();
        await svc.UpdateModifierOptionAsync(admin, freeGroup.Id, g0.Id,
            new ModifierOptionInput(g0.Name, 0m, 2, 0, "Porção 30g", granolaSupplyId, 30m, "g", 10m));
        var p0 = paidGroup.Options.First();
        await svc.UpdateModifierOptionAsync(admin, paidGroup.Id, p0.Id,
            new ModifierOptionInput(p0.Name, 5.50m, 2, 0, "Colher 40g", nutellaSupplyId, 40m, "g", 10m));

        var freeIds = freeGroup.Options.Take(4).Select(o => o.Id).ToList();

        // Copo 300 ml tem limite grátis 3 -> escolher 4 gratuitos deve ser bloqueado
        var selections300Overflow = new List<CatalogModifierSelection> { new(baseGroup.Id, baseOpt.Id, 1) };
        selections300Overflow.AddRange(freeIds.Select(id => new CatalogModifierSelection(freeGroup.Id, id, 1)));
        var blockedQuote = await svc.QuoteProductItemAsync(acai.Id, v300.Id, selections300Overflow);
        Assert.True(blockedQuote.IsFailure);

        // Copo 500 ml permite até 5 gratuitos + adicional pago -> sucesso com custo + perda calculados
        var selections500 = new List<CatalogModifierSelection> { new(baseGroup.Id, baseOpt.Id, 1) };
        selections500.AddRange(freeIds.Select(id => new CatalogModifierSelection(freeGroup.Id, id, 1)));
        selections500.Add(new CatalogModifierSelection(paidGroup.Id, p0.Id, 1));
        var okQuote = await svc.QuoteProductItemAsync(acai.Id, v500.Id, selections500);

        Assert.True(okQuote.IsSuccess);
        Assert.Equal(v500.Price.Amount + baseOpt.AdditionalPrice.Amount + 5.50m, okQuote.Value.UnitPrice.Amount);
        // Custo esperado: Granola (30 * 1.10 * 0.04 = 1.32) + Nutella (40 * 1.10 * 0.12 = 5.28) = 6.60
        Assert.Equal(6.60m, okQuote.Value.EstimatedCostAmount);
        Assert.Equal(2, okQuote.Value.CustomIngredients!.Count);
    }

    [Fact]
    public async Task Pizzaria_ShouldChargeHighestFlavor_PlusStuffedCrust_AndEnforceMaxFlavors()
    {
        var (_, _, prodRepo, modRepo, _, admin) = BuildHarness();
        var (svc, tpl, pRepo, mRepo, _, adm) = BuildHarness();
        var res = await tpl.ApplyTemplateAsync(adm, "pizzaria");
        Assert.True(res.IsSuccess);

        var pizza = pRepo.Items.Single();
        var grande = pizza.Variations.First(v => v.Name.Contains("Grande")); // MaxFlavors = 2, Preço base = 52.00
        var broto = pizza.Variations.First(v => v.Name.Contains("Broto"));   // MaxFlavors = 1

        var flavorsGroup = mRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.FlavorSelection);
        var crustGroup = mRepo.Items.First(g => g.Name.Contains("Borda"));
        var cheapestFlavor = flavorsGroup.Options.OrderBy(o => o.AdditionalPrice.Amount).First();
        var priceyFlavor = flavorsGroup.Options.OrderByDescending(o => o.AdditionalPrice.Amount).First();
        var cheddarCrust = crustGroup.Options.First(o => o.AdditionalPrice.Amount > 0m);

        // Broto só aceita 1 sabor -> escolher 2 deve falhar
        var tooManyForBroto = await svc.QuoteProductItemAsync(pizza.Id, broto.Id,
        [
            new(flavorsGroup.Id, cheapestFlavor.Id, 1),
            new(flavorsGroup.Id, priceyFlavor.Id, 1)
        ]);
        Assert.True(tooManyForBroto.IsFailure);

        // Grande meia a meia (FlavorHighestPrice) + Borda Recheada -> cobra maior sabor + borda
        var halfHalfQuote = await svc.QuoteProductItemAsync(pizza.Id, grande.Id,
        [
            new(flavorsGroup.Id, cheapestFlavor.Id, 1),
            new(flavorsGroup.Id, priceyFlavor.Id, 1),
            new(crustGroup.Id, cheddarCrust.Id, 1)
        ]);
        Assert.True(halfHalfQuote.IsSuccess);
        Assert.Equal(priceyFlavor.AdditionalPrice.Amount + cheddarCrust.AdditionalPrice.Amount, halfHalfQuote.Value.UnitPrice.Amount);
    }

    [Fact]
    public async Task Hamburgueria_ShouldEnforceRequiredChoices_AndBlockIncompatibleOptions()
    {
        var (svc, tpl, prodRepo, modRepo, _, admin) = BuildHarness();
        var res = await tpl.ApplyTemplateAsync(admin, "hamburgueria");
        Assert.True(res.IsSuccess);

        var burger = prodRepo.Items.Single();
        var burgerVarId = burger.Variations.First().Id;
        var burgerVarPrice = burger.Variations.First().Price.Amount;
        var breadGroup = modRepo.Items.First(g => g.Name.Contains("Pão"));
        var meatGroup = modRepo.Items.First(g => g.Name.Contains("Ponto"));
        var meatTypeGroup = modRepo.Items.First(g => g.Name.Contains("Carne") && !g.Name.Contains("Ponto"));
        var extrasGroup = modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.PaidAddon);

        // Configurar incompatibilidade entre opção vegana e ponto mal passado:
        var malPassado = meatGroup.Options.First(o => o.Name.Contains("Mal"));
        var veganSubst = modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.Substitution).Options.First();
        await svc.UpdateModifierOptionAsync(admin, modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.Substitution).Id, veganSubst.Id,
            new ModifierOptionInput(veganSubst.Name, veganSubst.AdditionalPrice.Amount, 1, 0, veganSubst.Description, IncompatibleOptionIdsCsv: malPassado.Id.ToString()));

        // Obrigatório: omitir pão ou ponto falha
        var missingRequired = await svc.QuoteProductItemAsync(burger.Id, burgerVarId, []);
        Assert.True(missingRequired.IsFailure);

        // Combinação inválida: hambúrguer vegano + carne mal passada -> bloqueado
        var subGroup = modRepo.Items.First(g => g.SelectionKind == ModifierGroupKind.Substitution);
        var invalidCombo = await svc.QuoteProductItemAsync(burger.Id, burgerVarId,
        [
            new(breadGroup.Id, breadGroup.Options.First().Id, 1),
            new(meatTypeGroup.Id, meatTypeGroup.Options.First().Id, 1),
            new(meatGroup.Id, malPassado.Id, 1),
            new(subGroup.Id, veganSubst.Id, 1)
        ]);
        Assert.True(invalidCombo.IsFailure);
        Assert.Equal("Catalog.IncompatibleOptions", invalidCombo.Error.Code);

        // Combinação válida + adicional pago
        var aoPonto = meatGroup.Options.First(o => !o.Name.Contains("Mal"));
        var extra = extrasGroup.Options.First(o => o.AdditionalPrice.Amount > 0m);
        var valid = await svc.QuoteProductItemAsync(burger.Id, burgerVarId,
        [
            new(breadGroup.Id, breadGroup.Options.First().Id, 1),
            new(meatTypeGroup.Id, meatTypeGroup.Options.First().Id, 1),
            new(meatGroup.Id, aoPonto.Id, 1),
            new(extrasGroup.Id, extra.Id, 1)
        ]);
        Assert.True(valid.IsSuccess);
        Assert.Equal(burgerVarPrice + aoPonto.AdditionalPrice.Amount + extra.AdditionalPrice.Amount, valid.Value.UnitPrice.Amount);
    }

    [Theory]
    [InlineData("pastelaria")]
    [InlineData("sorveteria")]
    [InlineData("restaurante")]
    [InlineData("lanchonete")]
    public async Task OtherSegments_Templates_ShouldProvisionAndQuoteCorrectly(string segmentCode)
    {
        var (svc, tpl, prodRepo, modRepo, _, admin) = BuildHarness();
        var res = await tpl.ApplyTemplateAsync(admin, segmentCode);
        Assert.True(res.IsSuccess);
        Assert.NotEmpty(prodRepo.Items);

        var prod = prodRepo.Items.First();
        var variation = prod.Variations.FirstOrDefault();
        var requiredSelections = new List<CatalogModifierSelection>();
        foreach (var link in prod.ModifierLinks)
        {
            var g = modRepo.Items.First(x => x.Id == link.ModifierGroupId);
            if (g.IsRequired && g.MinSelections > 0)
            {
                requiredSelections.Add(new(g.Id, g.Options.First().Id, g.MinSelections));
            }
        }

        var quote = await svc.QuoteProductItemAsync(prod.Id, variation?.Id, requiredSelections);
        Assert.True(quote.IsSuccess);
        Assert.True(quote.Value.UnitPrice.Amount > 0m);
    }

    [Fact]
    public void StoreOperatingHoursHelper_ShouldEvaluateOpenClosedAndTemporaryPause()
    {
        // Quarta-feira 14:00 (DayOfWeek = 3)
        var wedAfternoon = new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.FromHours(-3));
        var openEval = StoreOperatingHoursHelper.Evaluate(null, false, null, wedAfternoon);
        Assert.True(openEval.IsOpenNow);
        Assert.Equal("ABERTA AGORA", openEval.StatusLabel);

        // Pausa manual sobrescreve horário aberto
        var pausedEval = StoreOperatingHoursHelper.Evaluate(null, true, "Falta de energia", wedAfternoon);
        Assert.False(pausedEval.IsOpenNow);
        Assert.True(pausedEval.IsTemporarilyPaused);
        Assert.Equal("LOJA PAUSADA", pausedEval.StatusLabel);

        // Madrugada 03:00 (fora do padrão 11:00-23:00)
        var lateNight = new DateTimeOffset(2026, 11, 25, 3, 0, 0, TimeSpan.FromHours(-3));
        var closedEval = StoreOperatingHoursHelper.Evaluate(null, false, null, lateNight);
        Assert.False(closedEval.IsOpenNow);
    }
}
