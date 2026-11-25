using System.Text.Json;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Application.Services;

public sealed record EffectiveProductPrice(
    Guid ProductId,
    Money OriginalPrice,
    Money FinalPrice,
    string? AppliedPromotionName,
    bool CanBeOrdered);

/// <summary>
/// Caso de uso de gestão do catálogo por estabelecimento com isolamento multi-tenant,
/// controle granular de permissões e preservação histórica (Seção 12).
/// </summary>
public sealed class CatalogManagementService : ICatalogItemPricingService
{
    private readonly ICategoryRepository _categoryRepo;
    private readonly IProductRepository _productRepo;
    private readonly IModifierGroupRepository _modifierRepo;
    private readonly IComboRepository _comboRepo;
    private readonly IPromotionRepository _promotionRepo;
    private readonly IProductHistoryReferenceChecker _historyChecker;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public CatalogManagementService(
        ICategoryRepository categoryRepo,
        IProductRepository productRepo,
        IModifierGroupRepository modifierRepo,
        IComboRepository comboRepo,
        IPromotionRepository promotionRepo,
        IProductHistoryReferenceChecker historyChecker,
        ITenantContext tenantContext,
        IClock clock)
    {
        _categoryRepo = categoryRepo;
        _productRepo = productRepo;
        _modifierRepo = modifierRepo;
        _comboRepo = comboRepo;
        _promotionRepo = promotionRepo;
        _historyChecker = historyChecker;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<Category>> CreateCategoryAsync(
        User actor,
        string name,
        string? description = null,
        string? imageUrl = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Category>(guard.Error);

        var result = Category.Create(guard.Value, name, description, imageUrl, displayOrder);
        if (result.IsFailure)
            return result;

        await _categoryRepo.AddAsync(result.Value, cancellationToken);
        await _categoryRepo.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<Result> ReorderCategoriesAsync(
        User actor,
        IReadOnlyDictionary<Guid, int> newOrders,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var categories = await _categoryRepo.ListOrderedAsync(cancellationToken);
        foreach (var category in categories)
        {
            TenantGuard.EnsureOwnership(_tenantContext, category);
            if (newOrders.TryGetValue(category.Id, out var order))
                category.ChangeOrder(order);
        }

        await _categoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<Product>> CreateProductAsync(
        User actor,
        Guid categoryId,
        string name,
        decimal basePriceAmount,
        string? description = null,
        string? sku = null,
        ProductSaleMode saleMode = ProductSaleMode.Unit,
        bool isFeatured = false,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Product>(guard.Error);

        var category = await _categoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (category is null)
            return Result.Failure<Product>(Error.NotFound("Category.NotFound", "Categoria não encontrada neste estabelecimento."));

        TenantGuard.EnsureOwnership(_tenantContext, category);

        var priceResult = Money.Create(basePriceAmount);
        if (priceResult.IsFailure)
            return Result.Failure<Product>(priceResult.Error);

        var productResult = Product.Create(guard.Value, categoryId, name, priceResult.Value, description, sku, saleMode, isFeatured, displayOrder);
        if (productResult.IsFailure)
            return productResult;

        await _productRepo.AddAsync(productResult.Value, cancellationToken);
        await _productRepo.SaveChangesAsync(cancellationToken);
        return productResult;
    }

    public async Task<Result<ProductImage>> AddProductImageAsync(
        User actor,
        Guid productId,
        string imageUrl,
        int displayOrder = 0,
        bool isPrimary = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ProductImage>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<ProductImage>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);

        var imageResult = product.AddImage(imageUrl, displayOrder, isPrimary);
        if (imageResult.IsFailure)
            return imageResult;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return imageResult;
    }

    public async Task<Result<ProductVariation>> AddProductVariationAsync(
        User actor,
        Guid productId,
        string variationName,
        decimal priceAmount,
        string? sku = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ProductVariation>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<ProductVariation>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);

        var price = Money.Create(priceAmount);
        if (price.IsFailure)
            return Result.Failure<ProductVariation>(price.Error);

        var variation = product.AddVariation(variationName, price.Value, sku, displayOrder);
        if (variation.IsFailure)
            return variation;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return variation;
    }

    public async Task<Result> PauseProductAsync(User actor, Guid productId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        product.Pause();
        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReactivateProductAsync(User actor, Guid productId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        var reactivate = product.Reactivate();
        if (reactivate.IsFailure)
            return reactivate;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DeletionOutcome>> DeleteOrArchiveProductAsync(
        User actor,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return Result.Failure<DeletionOutcome>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<DeletionOutcome>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);

        var hasHistory = await _historyChecker.HasHistoricalReferencesAsync(guard.Value, productId, cancellationToken);
        var outcome = product.PrepareDeletion(hasHistory);
        if (outcome == DeletionOutcome.PhysicallyDeleted)
            _productRepo.Remove(product);

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(outcome);
    }

    public async Task<Result<ModifierGroup>> CreateModifierGroupAsync(
        User actor,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        IEnumerable<(string Name, decimal AdditionalPrice, int MaxQuantity)> options,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<ModifierGroup>(guard.Error);

        var groupResult = ModifierGroup.Create(guard.Value, name, type, minSelections, maxSelections, isRequired, displayOrder);
        if (groupResult.IsFailure)
            return groupResult;

        var order = 0;
        foreach (var opt in options)
        {
            var price = Money.Create(opt.AdditionalPrice);
            if (price.IsFailure)
                return Result.Failure<ModifierGroup>(price.Error);

            var added = groupResult.Value.AddOption(opt.Name, price.Value, opt.MaxQuantity, order++);
            if (added.IsFailure)
                return Result.Failure<ModifierGroup>(added.Error);
        }

        await _modifierRepo.AddAsync(groupResult.Value, cancellationToken);
        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return groupResult;
    }

    public async Task<Result> AttachModifierGroupToProductAsync(
        User actor,
        Guid productId,
        Guid modifierGroupId,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        var group = await _modifierRepo.FindByIdAsync(modifierGroupId, cancellationToken);
        if (product is null || group is null)
            return Result.Failure(Error.NotFound("Catalog.EntityNotFound", "Produto ou grupo de adicionais não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        TenantGuard.EnsureOwnership(_tenantContext, group);

        product.AttachModifierGroup(modifierGroupId, displayOrder);
        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<Combo>> CreateComboAsync(
        User actor,
        string name,
        decimal comboPriceAmount,
        IReadOnlyList<(Guid ProductId, int Quantity, Guid? VariationId, string? Notes)> items,
        string? description = null,
        string? primaryImageUrl = null,
        bool isFeatured = false,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Combo>(guard.Error);

        if (items.Count == 0)
            return Result.Failure<Combo>(Error.Validation("Combo.EmptyItems", "Combo exige pelo menos 1 produto componente."));

        var price = Money.Create(comboPriceAmount);
        if (price.IsFailure)
            return Result.Failure<Combo>(price.Error);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _productRepo.ListByIdsAsync(productIds, cancellationToken);
        if (products.Count != productIds.Count)
            return Result.Failure<Combo>(Error.NotFound("Combo.ProductsMissing", "Um ou mais produtos do combo não pertencem a este estabelecimento."));

        var comboResult = Combo.Create(guard.Value, name, price.Value, description, primaryImageUrl, isFeatured, displayOrder);
        if (comboResult.IsFailure)
            return comboResult;

        foreach (var item in items)
        {
            var addItem = comboResult.Value.AddItem(item.ProductId, item.Quantity, item.VariationId, item.Notes);
            if (addItem.IsFailure)
                return Result.Failure<Combo>(addItem.Error);
        }

        await _comboRepo.AddAsync(comboResult.Value, cancellationToken);
        await _comboRepo.SaveChangesAsync(cancellationToken);
        return comboResult;
    }

    public async Task<Result<Promotion>> CreatePromotionAsync(
        User actor,
        string name,
        PromotionDiscountType discountType,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        IEnumerable<(PromotionTargetType TargetType, Guid TargetId)> targets,
        bool canCombineWithCoupons = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Promotion>(guard.Error);

        var promoResult = Promotion.Create(guard.Value, name, discountType, discountValue, startsAtUtc, endsAtUtc, canCombineWithCoupons);
        if (promoResult.IsFailure)
            return promoResult;

        foreach (var (targetType, targetId) in targets)
            promoResult.Value.AddTarget(targetType, targetId);

        await _promotionRepo.AddAsync(promoResult.Value, cancellationToken);
        await _promotionRepo.SaveChangesAsync(cancellationToken);
        return promoResult;
    }

    public async Task<Result<EffectiveProductPrice>> GetEffectiveProductPriceAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<EffectiveProductPrice>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);

        var utcNow = _clock.UtcNow;
        var promotions = await _promotionRepo.ListActiveCandidatesAsync(cancellationToken);

        var bestPrice = product.BasePrice;
        string? appliedPromo = null;

        foreach (var promo in promotions.Where(p => p.IsActiveAt(utcNow) && p.AppliesTo(product)))
        {
            var candidate = promo.CalculatePromotionalPrice(product.BasePrice);
            if (candidate.Amount < bestPrice.Amount)
            {
                bestPrice = candidate;
                appliedPromo = promo.Name;
            }
        }

        return Result.Success(new EffectiveProductPrice(
            product.Id,
            product.BasePrice,
            bestPrice,
            appliedPromo,
            product.CanReceiveOrders()));
    }

    public async Task<Result<CatalogProductQuote>> QuoteProductItemAsync(
        Guid productId,
        Guid? variationId,
        IReadOnlyList<CatalogModifierSelection>? modifiers,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<CatalogProductQuote>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        if (!product.CanReceiveOrders())
            return Result.Failure<CatalogProductQuote>(Error.Conflict("Catalog.ProductUnavailable", $"Produto '{product.Name}' encontra-se indisponível para pedidos."));

        Money basePrice = product.BasePrice;
        string? variationName = null;
        if (variationId.HasValue && variationId.Value != Guid.Empty)
        {
            var variation = product.Variations.FirstOrDefault(v => v.Id == variationId.Value && v.IsActive);
            if (variation is null)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.VariationUnavailable", "Variação selecionada indisponível."));

            basePrice = variation.Price;
            variationName = variation.Name;
        }

        var utcNow = _clock.UtcNow;
        var promotions = await _promotionRepo.ListActiveCandidatesAsync(cancellationToken);
        var effectivePrice = basePrice;
        Promotion? appliedPromo = null;

        foreach (var promo in promotions.Where(p => p.IsActiveAt(utcNow) && p.AppliesTo(product)))
        {
            var candidate = promo.CalculatePromotionalPrice(basePrice);
            if (candidate.Amount < effectivePrice.Amount)
            {
                effectivePrice = candidate;
                appliedPromo = promo;
            }
        }

        var selectionList = modifiers ?? [];
        var linkedGroupIds = product.ModifierLinks.Select(l => l.ModifierGroupId).ToHashSet();
        var loadedGroups = new Dictionary<Guid, ModifierGroup>();
        foreach (var groupId in linkedGroupIds)
        {
            var g = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
            if (g is not null)
                loadedGroups[g.Id] = g;
        }

        var modifierCountsByGroup = selectionList
            .GroupBy(s => s.ModifierGroupId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var (groupId, group) in loadedGroups)
        {
            var chosenCount = modifierCountsByGroup.GetValueOrDefault(groupId, 0);
            if (group.IsRequired && chosenCount < group.MinSelections)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierMinRequired", $"Selecione pelo menos {group.MinSelections} item(ns) em '{group.Name}'."));
            if (group.MaxSelections > 0 && chosenCount > group.MaxSelections)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierMaxExceeded", $"Grupo '{group.Name}' permite no máximo {group.MaxSelections} seleção(ões)."));
        }

        var modifierExtra = Money.Zero(basePrice.Currency);
        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(variationName))
            summaryParts.Add($"Tamanho/Variação: {variationName}");

        var canonicalModifiers = new List<object>();
        foreach (var sel in selectionList
            .GroupBy(s => new { s.ModifierGroupId, s.ModifierOptionId })
            .Select(g => new { g.Key.ModifierGroupId, g.Key.ModifierOptionId, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.ModifierGroupId)
            .ThenBy(x => x.ModifierOptionId))
        {
            if (sel.Quantity <= 0 || !linkedGroupIds.Contains(sel.ModifierGroupId) || !loadedGroups.TryGetValue(sel.ModifierGroupId, out var group))
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.InvalidModifier", "Adicional/complemento inválido para este produto."));

            var option = group.Options.FirstOrDefault(o => o.Id == sel.ModifierOptionId && o.IsAvailable);
            if (option is null)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierOptionUnavailable", "Opção adicional indisponível."));

            if (sel.Quantity > option.MaxQuantity)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierOptionMaxExceeded", $"Opção '{option.Name}' permite no máximo {option.MaxQuantity}."));

            modifierExtra = modifierExtra.Add(option.AdditionalPrice.Multiply(sel.Quantity));
            summaryParts.Add($"{sel.Quantity}x {option.Name}");
            canonicalModifiers.Add(new { groupId = group.Id, optionId = option.Id, name = option.Name, qty = sel.Quantity, unitPrice = option.AdditionalPrice.Amount });
        }

        var unitPrice = effectivePrice.Add(modifierExtra);
        var canonicalJson = JsonSerializer.Serialize(new { variationId, modifiers = canonicalModifiers });
        var summaryText = summaryParts.Count == 0 ? "Padrão" : string.Join(" | ", summaryParts);

        return Result.Success(new CatalogProductQuote(
            product.Id,
            product.CategoryId,
            product.Name,
            variationId == Guid.Empty ? null : variationId,
            variationName,
            unitPrice,
            appliedPromo is not null && !appliedPromo.CanCombineWithCoupons,
            summaryText,
            canonicalJson));
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar catálogo de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Catalog, action))
            return Result.Failure<Guid>(Error.Forbidden("Catalog.Forbidden", "Permissão insuficiente para operar catálogo."));

        return Result.Success(currentTenantId);
    }
}
