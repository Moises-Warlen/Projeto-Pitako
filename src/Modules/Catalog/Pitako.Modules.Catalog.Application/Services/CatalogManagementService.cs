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

public sealed record ModifierOptionInput(
    string Name,
    decimal AdditionalPrice,
    int MaxQuantity = 1,
    int DisplayOrder = -1,
    string? Description = null,
    Guid? SupplyItemId = null,
    decimal ConsumptionQuantity = 0m,
    string? ConsumptionUnitCode = null,
    decimal LossPercent = 0m,
    string? ReplacesLabel = null,
    string? IncompatibleOptionIdsCsv = null);

/// <summary>
/// Caso de uso de gestão do catálogo por estabelecimento com isolamento multi-tenant,
/// controle granular de permissões e preservação histórica (Seção 12).
/// </summary>
public sealed class CatalogManagementService : IUniversalCatalogQuoteService
{
    private readonly ICategoryRepository _categoryRepo;
    private readonly IProductRepository _productRepo;
    private readonly IModifierGroupRepository _modifierRepo;
    private readonly IComboRepository _comboRepo;
    private readonly IPromotionRepository _promotionRepo;
    private readonly IProductHistoryReferenceChecker _historyChecker;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly ICatalogSupplyCostLookup? _supplyLookup;

    public CatalogManagementService(
        ICategoryRepository categoryRepo,
        IProductRepository productRepo,
        IModifierGroupRepository modifierRepo,
        IComboRepository comboRepo,
        IPromotionRepository promotionRepo,
        IProductHistoryReferenceChecker historyChecker,
        ITenantContext tenantContext,
        IClock clock,
        ICatalogSupplyCostLookup? supplyLookup = null)
    {
        _categoryRepo = categoryRepo;
        _productRepo = productRepo;
        _modifierRepo = modifierRepo;
        _comboRepo = comboRepo;
        _promotionRepo = promotionRepo;
        _historyChecker = historyChecker;
        _tenantContext = tenantContext;
        _clock = clock;
        _supplyLookup = supplyLookup;
    }

    public Task<Result<Category>> CreateCategoryAsync(
        User actor,
        string name,
        string? description = null,
        string? imageUrl = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default) =>
        CreateCategoryAsync(actor, name, description, imageUrl, displayOrder, null, null, cancellationToken);

    public async Task<Result<Category>> CreateCategoryAsync(
        User actor,
        string name,
        string? description,
        string? imageUrl,
        int displayOrder,
        string? segmentTemplateCode,
        string? ruleNotes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Category>(guard.Error);

        var result = Category.Create(guard.Value, name, description, imageUrl, displayOrder, segmentTemplateCode, ruleNotes);
        if (result.IsFailure)
            return result;

        await _categoryRepo.AddAsync(result.Value, cancellationToken);
        await _categoryRepo.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<Result<Category>> UpdateCategoryAsync(
        User actor,
        Guid categoryId,
        string name,
        string? description,
        string? imageUrl,
        int displayOrder,
        bool isActive = true,
        string? segmentTemplateCode = null,
        string? ruleNotes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Category>(guard.Error);

        var category = await _categoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (category is null)
            return Result.Failure<Category>(Error.NotFound("Category.NotFound", "Categoria não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, category);
        var upd = category.Update(name, description, imageUrl, displayOrder, segmentTemplateCode, ruleNotes);
        if (upd.IsFailure)
            return Result.Failure<Category>(upd.Error);

        if (isActive) category.Reactivate();
        else category.Pause();

        await _categoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(category);
    }

    public async Task<Result> DeleteCategoryAsync(User actor, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var category = await _categoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (category is null)
            return Result.Failure(Error.NotFound("Category.NotFound", "Categoria não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, category);

        var products = await _productRepo.ListAllOrderedAsync(cancellationToken);
        if (products.Any(p => p.CategoryId == categoryId && p.Status != CatalogItemStatus.Archived))
            return Result.Failure(Error.Conflict("Category.HasProducts", "Exclua ou mova os produtos desta categoria antes de excluí-la."));

        _categoryRepo.Remove(category);
        await _categoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

    public Task<Result<Product>> CreateProductAsync(
        User actor,
        Guid categoryId,
        string name,
        decimal basePriceAmount,
        string? description = null,
        string? sku = null,
        ProductSaleMode saleMode = ProductSaleMode.Unit,
        bool isFeatured = false,
        int displayOrder = 0,
        CancellationToken cancellationToken = default) =>
        CreateProductAsync(actor, categoryId, name, basePriceAmount, description, sku, saleMode, isFeatured, displayOrder, false, false, ProductPricingStrategy.FixedPlusAddons, null, null, null, true, cancellationToken);

    public async Task<Result<Product>> CreateProductAsync(
        User actor,
        Guid categoryId,
        string name,
        decimal basePriceAmount,
        string? description,
        string? sku,
        ProductSaleMode saleMode,
        bool isFeatured,
        int displayOrder,
        bool isNew,
        bool isRecommended = false,
        ProductPricingStrategy pricingStrategy = ProductPricingStrategy.FixedPlusAddons,
        string? tagsCsv = null,
        int? preparationMinutes = null,
        string? customTemplateKey = null,
        bool allowsNotes = true,
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

        var productResult = Product.Create(
            guard.Value,
            categoryId,
            name,
            priceResult.Value,
            description,
            sku,
            saleMode,
            isFeatured,
            displayOrder,
            isNew,
            isRecommended,
            pricingStrategy,
            tagsCsv,
            preparationMinutes,
            customTemplateKey,
            allowsNotes);
        if (productResult.IsFailure)
            return productResult;

        await _productRepo.AddAsync(productResult.Value, cancellationToken);
        await _productRepo.SaveChangesAsync(cancellationToken);
        return productResult;
    }

    public async Task<Result<Product>> UpdateProductAsync(
        User actor,
        Guid productId,
        Guid categoryId,
        string name,
        decimal basePriceAmount,
        string? description,
        string? sku,
        bool isFeatured,
        int displayOrder,
        ProductSaleMode saleMode,
        bool isNew,
        bool isRecommended,
        ProductPricingStrategy pricingStrategy,
        string? tagsCsv = null,
        int? preparationMinutes = null,
        string? customTemplateKey = null,
        bool allowsNotes = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Product>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<Product>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);

        var category = await _categoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (category is null)
            return Result.Failure<Product>(Error.NotFound("Category.NotFound", "Categoria não encontrada neste estabelecimento."));
        TenantGuard.EnsureOwnership(_tenantContext, category);

        var priceResult = Money.Create(basePriceAmount);
        if (priceResult.IsFailure)
            return Result.Failure<Product>(priceResult.Error);

        var upd = product.UpdateDetails(
            categoryId,
            name,
            priceResult.Value,
            description,
            sku,
            isFeatured,
            displayOrder,
            saleMode,
            isNew,
            isRecommended,
            pricingStrategy,
            tagsCsv,
            preparationMinutes,
            customTemplateKey,
            allowsNotes);

        if (upd.IsFailure)
            return Result.Failure<Product>(upd.Error);

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(product);
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

    public async Task<Result> RemoveProductImageAsync(User actor, Guid productId, Guid imageId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        var res = product.RemoveImage(imageId);
        if (res.IsFailure)
            return res;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ProductVariation>> AddProductVariationAsync(
        User actor,
        Guid productId,
        string variationName,
        decimal priceAmount,
        string? sku = null,
        int displayOrder = 0,
        string? description = null,
        decimal? measureAmount = null,
        string? measureUnit = null,
        int maxFlavors = 1,
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

        var variation = product.AddVariation(variationName, price.Value, sku, displayOrder, description, measureAmount, measureUnit, maxFlavors);
        if (variation.IsFailure)
            return variation;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return variation;
    }

    public async Task<Result<ProductVariation>> UpdateProductVariationAsync(
        User actor,
        Guid productId,
        Guid variationId,
        string variationName,
        decimal priceAmount,
        string? sku = null,
        int displayOrder = 0,
        string? description = null,
        decimal? measureAmount = null,
        string? measureUnit = null,
        int maxFlavors = 1,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ProductVariation>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<ProductVariation>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        var variation = product.Variations.FirstOrDefault(v => v.Id == variationId);
        if (variation is null)
            return Result.Failure<ProductVariation>(Error.NotFound("Product.VariationNotFound", "Variação não encontrada."));

        var price = Money.Create(priceAmount);
        if (price.IsFailure)
            return Result.Failure<ProductVariation>(price.Error);

        var res = variation.UpdateDetails(variationName, price.Value, sku, displayOrder, description, measureAmount, measureUnit, maxFlavors);
        if (res.IsFailure)
            return Result.Failure<ProductVariation>(res.Error);

        if (isActive) variation.Reactivate();
        else variation.Pause();

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(variation);
    }

    public async Task<Result> RemoveProductVariationAsync(User actor, Guid productId, Guid variationId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        var res = product.RemoveVariation(variationId);
        if (res.IsFailure)
            return res;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
        return await CreateModifierGroupDetailedAsync(
            actor,
            name,
            type,
            minSelections,
            maxSelections,
            isRequired,
            options.Select((o, idx) => new ModifierOptionInput(o.Name, o.AdditionalPrice, o.MaxQuantity, idx)),
            displayOrder,
            null,
            0,
            OptionCountingMode.ByTotalQuantity,
            null,
            cancellationToken);
    }

    public async Task<Result<ModifierGroup>> CreateModifierGroupDetailedAsync(
        User actor,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        IEnumerable<ModifierOptionInput> options,
        int displayOrder = 0,
        ModifierGroupKind? selectionKind = null,
        int freeAllowance = 0,
        OptionCountingMode countingMode = OptionCountingMode.ByTotalQuantity,
        string? categoryTag = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<ModifierGroup>(guard.Error);

        var groupResult = ModifierGroup.Create(
            guard.Value,
            name,
            type,
            minSelections,
            maxSelections,
            isRequired,
            displayOrder,
            selectionKind,
            freeAllowance,
            countingMode,
            categoryTag);
        if (groupResult.IsFailure)
            return groupResult;

        var order = 0;
        foreach (var opt in options)
        {
            var price = Money.Create(opt.AdditionalPrice);
            if (price.IsFailure)
                return Result.Failure<ModifierGroup>(price.Error);

            var added = groupResult.Value.AddOption(
                opt.Name,
                price.Value,
                opt.MaxQuantity,
                opt.DisplayOrder >= 0 ? opt.DisplayOrder : order++,
                opt.Description,
                opt.SupplyItemId,
                opt.ConsumptionQuantity,
                opt.ConsumptionUnitCode,
                opt.LossPercent,
                opt.ReplacesLabel,
                opt.IncompatibleOptionIdsCsv);
            if (added.IsFailure)
                return Result.Failure<ModifierGroup>(added.Error);
        }

        await _modifierRepo.AddAsync(groupResult.Value, cancellationToken);
        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return groupResult;
    }

    public async Task<Result<ModifierGroup>> UpdateModifierGroupAsync(
        User actor,
        Guid groupId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder,
        ModifierGroupKind selectionKind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string? categoryTag,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ModifierGroup>(guard.Error);

        var group = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
        if (group is null)
            return Result.Failure<ModifierGroup>(Error.NotFound("ModifierGroup.NotFound", "Grupo de opções não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, group);
        var res = group.UpdateConfiguration(name, type, minSelections, maxSelections, isRequired, displayOrder, selectionKind, freeAllowance, countingMode, categoryTag);
        if (res.IsFailure)
            return Result.Failure<ModifierGroup>(res.Error);

        if (isActive) group.Reactivate();
        else group.Pause();

        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(group);
    }

    public async Task<Result> DeleteModifierGroupAsync(User actor, Guid groupId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var group = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
        if (group is null)
            return Result.Failure(Error.NotFound("ModifierGroup.NotFound", "Grupo de opções não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, group);
        var products = await _productRepo.ListAllOrderedAsync(cancellationToken);
        foreach (var prod in products.Where(p => p.ModifierLinks.Any(l => l.ModifierGroupId == groupId)))
            prod.DetachModifierGroup(groupId);

        _modifierRepo.Remove(group);
        await _modifierRepo.SaveChangesAsync(cancellationToken);
        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ModifierOption>> AddModifierOptionAsync(
        User actor,
        Guid groupId,
        ModifierOptionInput input,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ModifierOption>(guard.Error);

        var group = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
        if (group is null)
            return Result.Failure<ModifierOption>(Error.NotFound("ModifierGroup.NotFound", "Grupo de opções não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, group);
        var price = Money.Create(input.AdditionalPrice);
        if (price.IsFailure)
            return Result.Failure<ModifierOption>(price.Error);

        var optRes = group.AddOption(
            input.Name,
            price.Value,
            input.MaxQuantity,
            input.DisplayOrder,
            input.Description,
            input.SupplyItemId,
            input.ConsumptionQuantity,
            input.ConsumptionUnitCode,
            input.LossPercent,
            input.ReplacesLabel,
            input.IncompatibleOptionIdsCsv);
        if (optRes.IsFailure)
            return optRes;

        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return optRes;
    }

    public async Task<Result<ModifierOption>> UpdateModifierOptionAsync(
        User actor,
        Guid groupId,
        Guid optionId,
        ModifierOptionInput input,
        bool isAvailable = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<ModifierOption>(guard.Error);

        var group = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
        if (group is null)
            return Result.Failure<ModifierOption>(Error.NotFound("ModifierGroup.NotFound", "Grupo de opções não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, group);
        var opt = group.Options.FirstOrDefault(o => o.Id == optionId);
        if (opt is null)
            return Result.Failure<ModifierOption>(Error.NotFound("ModifierOption.NotFound", "Opção não encontrada."));

        var price = Money.Create(input.AdditionalPrice);
        if (price.IsFailure)
            return Result.Failure<ModifierOption>(price.Error);

        var res = opt.Update(
            input.Name,
            price.Value,
            input.MaxQuantity,
            input.DisplayOrder,
            input.Description,
            input.SupplyItemId,
            input.ConsumptionQuantity,
            input.ConsumptionUnitCode,
            input.LossPercent,
            input.ReplacesLabel,
            input.IncompatibleOptionIdsCsv);
        if (res.IsFailure)
            return Result.Failure<ModifierOption>(res.Error);

        if (isAvailable) opt.Reactivate();
        else opt.Pause();

        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(opt);
    }

    public async Task<Result> RemoveModifierOptionAsync(User actor, Guid groupId, Guid optionId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var group = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
        if (group is null)
            return Result.Failure(Error.NotFound("ModifierGroup.NotFound", "Grupo de opções não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, group);
        var res = group.RemoveOption(optionId);
        if (res.IsFailure)
            return res;

        await _modifierRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

    public async Task<Result> DetachModifierGroupFromProductAsync(User actor, Guid productId, Guid modifierGroupId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        product.DetachModifierGroup(modifierGroupId);
        await _productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<VariationModifierRule>> SetVariationModifierRuleAsync(
        User actor,
        Guid productId,
        Guid? variationId,
        Guid modifierGroupId,
        int minSelections,
        int maxSelections,
        int freeAllowance,
        bool isRequired,
        bool isHidden = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<VariationModifierRule>(guard.Error);

        var product = await _productRepo.FindByIdAsync(productId, cancellationToken);
        if (product is null)
            return Result.Failure<VariationModifierRule>(Error.NotFound("Product.NotFound", "Produto não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, product);
        product.AttachModifierGroup(modifierGroupId, product.ModifierLinks.Count);

        var ruleRes = product.SetVariationRule(variationId, modifierGroupId, minSelections, maxSelections, freeAllowance, isRequired, isHidden);
        if (ruleRes.IsFailure)
            return ruleRes;

        await _productRepo.SaveChangesAsync(cancellationToken);
        return ruleRes;
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

    public async Task<Result<Combo>> UpdateComboAsync(
        User actor,
        Guid comboId,
        string name,
        decimal comboPriceAmount,
        IReadOnlyList<(Guid ProductId, int Quantity, Guid? VariationId, string? Notes)> items,
        string? description = null,
        string? primaryImageUrl = null,
        bool isFeatured = false,
        int displayOrder = 0,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Combo>(guard.Error);

        var combo = await _comboRepo.FindByIdAsync(comboId, cancellationToken);
        if (combo is null)
            return Result.Failure<Combo>(Error.NotFound("Combo.NotFound", "Combo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, combo);
        if (items.Count == 0)
            return Result.Failure<Combo>(Error.Validation("Combo.EmptyItems", "Combo exige pelo menos 1 produto componente."));

        var price = Money.Create(comboPriceAmount);
        if (price.IsFailure)
            return Result.Failure<Combo>(price.Error);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _productRepo.ListByIdsAsync(productIds, cancellationToken);
        if (products.Count != productIds.Count)
            return Result.Failure<Combo>(Error.NotFound("Combo.ProductsMissing", "Um ou mais produtos do combo não pertencem a este estabelecimento."));

        var upd = combo.UpdateDetails(name, price.Value, description, primaryImageUrl, isFeatured, displayOrder);
        if (upd.IsFailure)
            return Result.Failure<Combo>(upd.Error);

        combo.ClearItems();
        foreach (var item in items)
        {
            var addItem = combo.AddItem(item.ProductId, item.Quantity, item.VariationId, item.Notes);
            if (addItem.IsFailure)
                return Result.Failure<Combo>(addItem.Error);
        }

        if (isActive) combo.Reactivate();
        else combo.Pause();

        await _comboRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(combo);
    }

    public async Task<Result> DeleteComboAsync(User actor, Guid comboId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var combo = await _comboRepo.FindByIdAsync(comboId, cancellationToken);
        if (combo is null)
            return Result.Failure(Error.NotFound("Combo.NotFound", "Combo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, combo);
        _comboRepo.Remove(combo);
        await _comboRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

    public async Task<Result<Promotion>> UpdatePromotionAsync(
        User actor,
        Guid promotionId,
        string name,
        PromotionDiscountType discountType,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        IEnumerable<(PromotionTargetType TargetType, Guid TargetId)> targets,
        bool canCombineWithCoupons = false,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Promotion>(guard.Error);

        var promo = await _promotionRepo.FindByIdAsync(promotionId, cancellationToken);
        if (promo is null)
            return Result.Failure<Promotion>(Error.NotFound("Promotion.NotFound", "Promoção não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, promo);
        var upd = promo.Update(name, discountType, discountValue, startsAtUtc, endsAtUtc, canCombineWithCoupons);
        if (upd.IsFailure)
            return Result.Failure<Promotion>(upd.Error);

        promo.ClearTargets();
        foreach (var (targetType, targetId) in targets)
            promo.AddTarget(targetType, targetId);

        if (isActive) promo.Reactivate();
        else promo.Pause();

        await _promotionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(promo);
    }

    public async Task<Result> DeletePromotionAsync(User actor, Guid promotionId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var promo = await _promotionRepo.FindByIdAsync(promotionId, cancellationToken);
        if (promo is null)
            return Result.Failure(Error.NotFound("Promotion.NotFound", "Promoção não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, promo);
        _promotionRepo.Remove(promo);
        await _promotionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

    public async Task<Result<CatalogProductQuote>> QuoteComboAsync(Guid comboId, CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);
        var combo = await _comboRepo.FindByIdAsync(comboId, cancellationToken);
        if (combo is null)
            return Result.Failure<CatalogProductQuote>(Error.NotFound("Combo.NotFound", "Combo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, combo);
        var prodList = await _productRepo.ListByIdsAsync(combo.Items.Select(i => i.ProductId), cancellationToken);
        var prodMap = prodList.ToDictionary(p => p.Id);
        if (!combo.CanReceiveOrders(prodMap))
            return Result.Failure<CatalogProductQuote>(Error.Conflict("Combo.Unavailable", $"Combo '{combo.Name}' possui itens indisponíveis no momento."));

        var components = combo.Items.Select(i =>
        {
            var p = prodMap[i.ProductId];
            return new { productId = i.ProductId, variationId = i.VariationId, qty = i.Quantity, name = p.Name };
        }).ToList();

        var summary = "Combo: " + string.Join(" + ", combo.Items.Select(i => $"{i.Quantity}x {prodMap[i.ProductId].Name}"));
        var canonical = JsonSerializer.Serialize(new { comboId = combo.Id, items = components });

        return Result.Success(new CatalogProductQuote(
            combo.Id,
            prodList.First().CategoryId,
            combo.Name,
            null,
            null,
            combo.ComboPrice,
            false,
            summary,
            canonical));
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
        ProductVariation? chosenVariation = null;
        if (variationId.HasValue && variationId.Value != Guid.Empty)
        {
            chosenVariation = product.Variations.FirstOrDefault(v => v.Id == variationId.Value && v.IsActive);
            if (chosenVariation is null)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.VariationUnavailable", "Variação selecionada indisponível."));

            basePrice = chosenVariation.Price;
            variationName = chosenVariation.Name;
        }
        else if (product.Variations.Any(v => v.IsActive))
        {
            return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.VariationRequired", $"Selecione o tamanho/variação de '{product.Name}'."));
        }

        var selectionList = (modifiers ?? [])
            .GroupBy(s => new { s.ModifierGroupId, s.ModifierOptionId })
            .Select(g => new { g.Key.ModifierGroupId, g.Key.ModifierOptionId, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.ModifierGroupId)
            .ThenBy(x => x.ModifierOptionId)
            .ToList();

        var linkedGroupIds = product.ModifierLinks.Select(l => l.ModifierGroupId).ToHashSet();
        var loadedGroups = new Dictionary<Guid, ModifierGroup>();
        foreach (var groupId in linkedGroupIds)
        {
            var g = await _modifierRepo.FindByIdAsync(groupId, cancellationToken);
            if (g is not null && g.IsActive)
                loadedGroups[g.Id] = g;
        }

        var effectiveRules = new Dictionary<Guid, (int Min, int Max, int Free, bool IsRequired, bool IsHidden)>();
        foreach (var (groupId, group) in loadedGroups)
        {
            var rule = product.VariationRules.FirstOrDefault(r => r.ProductVariationId == chosenVariation?.Id && r.ModifierGroupId == groupId)
                ?? product.VariationRules.FirstOrDefault(r => r.ProductVariationId == null && r.ModifierGroupId == groupId);
            effectiveRules[groupId] = rule is not null
                ? (rule.MinSelections, rule.MaxSelections, rule.FreeAllowance, rule.IsRequired, rule.IsHidden)
                : (group.MinSelections, group.MaxSelections, group.FreeAllowance, group.IsRequired, false);
        }

        var resolvedSelections = new List<(ModifierGroup Group, ModifierOption Option, int Quantity)>();
        var selectedOptionIds = new HashSet<Guid>();

        foreach (var sel in selectionList)
        {
            if (sel.Quantity <= 0 || !linkedGroupIds.Contains(sel.ModifierGroupId) || !loadedGroups.TryGetValue(sel.ModifierGroupId, out var group))
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.InvalidModifier", "Adicional/complemento inválido para este produto."));

            if (effectiveRules[sel.ModifierGroupId].IsHidden)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierHiddenForVariation", $"Grupo '{group.Name}' não está disponível para este tamanho."));

            var option = group.Options.FirstOrDefault(o => o.Id == sel.ModifierOptionId && o.IsAvailable);
            if (option is null)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierOptionUnavailable", $"Opção selecionada em '{group.Name}' está indisponível."));

            if (sel.Quantity > option.MaxQuantity)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierOptionMaxExceeded", $"Opção '{option.Name}' permite no máximo {option.MaxQuantity}."));

            resolvedSelections.Add((group, option, sel.Quantity));
            selectedOptionIds.Add(option.Id);
        }

        foreach (var (_, opt, _) in resolvedSelections)
        {
            if (string.IsNullOrWhiteSpace(opt.IncompatibleOptionIdsCsv))
                continue;
            var conflicts = opt.IncompatibleOptionIdsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty && selectedOptionIds.Contains(g));
            if (conflicts.Any())
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.IncompatibleOptions", $"A opção '{opt.Name}' não pode ser combinada com outra seleção incompatível."));
        }

        foreach (var (groupId, group) in loadedGroups)
        {
            var r = effectiveRules[groupId];
            if (r.IsHidden) continue;

            var groupSels = resolvedSelections.Where(x => x.Group.Id == groupId).ToList();
            var chosenCount = group.CountingMode == OptionCountingMode.ByDistinctOption
                ? groupSels.Count
                : groupSels.Sum(x => x.Quantity);

            if (r.IsRequired && chosenCount < r.Min)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierMinRequired", $"Selecione pelo menos {r.Min} item(ns) em '{group.Name}'."));
            if (r.Max > 0 && chosenCount > r.Max)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.ModifierMaxExceeded", $"Grupo '{group.Name}' permite no máximo {r.Max} seleção(ões)."));

            if (group.SelectionKind == ModifierGroupKind.FlavorSelection && chosenVariation is not null && chosenCount > chosenVariation.MaxFlavors)
                return Result.Failure<CatalogProductQuote>(Error.Validation("Catalog.MaxFlavorsExceeded", $"O tamanho '{chosenVariation.Name}' permite no máximo {chosenVariation.MaxFlavors} sabor(es)."));
        }

        // Precificação de sabores por estratégia do produto (ex.: Pizzaria Meia-a-Meia: maior sabor ou média)
        var effectiveBasePrice = basePrice;
        var flavorSelections = resolvedSelections.Where(s => s.Group.SelectionKind == ModifierGroupKind.FlavorSelection).ToList();
        if (product.PricingStrategy != ProductPricingStrategy.FixedPlusAddons && flavorSelections.Count > 0)
        {
            var expandedFlavorPrices = new List<decimal>();
            foreach (var (_, opt, qty) in flavorSelections)
            {
                var pVal = opt.AdditionalPrice.Amount > 0m ? opt.AdditionalPrice.Amount : basePrice.Amount;
                for (var k = 0; k < qty; k++)
                    expandedFlavorPrices.Add(pVal);
            }

            if (expandedFlavorPrices.Count > 0)
            {
                var computed = product.PricingStrategy switch
                {
                    ProductPricingStrategy.FlavorHighestPrice => Math.Max(basePrice.Amount, expandedFlavorPrices.Max()),
                    ProductPricingStrategy.FlavorAveragePrice or ProductPricingStrategy.FlavorProportional => decimal.Round(expandedFlavorPrices.Average(), 2, MidpointRounding.AwayFromZero),
                    _ => basePrice.Amount
                };
                effectiveBasePrice = Money.Create(computed, basePrice.Currency).Value;
            }
        }

        var utcNow = _clock.UtcNow;
        var promotions = await _promotionRepo.ListActiveCandidatesAsync(cancellationToken);
        var effectivePriceAfterPromo = effectiveBasePrice;
        Promotion? appliedPromo = null;

        foreach (var promo in promotions.Where(p => p.IsActiveAt(utcNow) && p.AppliesTo(product)))
        {
            var candidate = promo.CalculatePromotionalPrice(effectiveBasePrice);
            if (candidate.Amount < effectivePriceAfterPromo.Amount)
            {
                effectivePriceAfterPromo = candidate;
                appliedPromo = promo;
            }
        }

        var supplyIds = resolvedSelections.Select(s => s.Option.SupplyItemId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var supplyCosts = (_supplyLookup is not null && supplyIds.Count > 0)
            ? await _supplyLookup.GetSupplyCostsAsync(supplyIds, cancellationToken)
            : new Dictionary<Guid, CatalogSupplyCostInfo>();

        var modifierExtra = Money.Zero(basePrice.Currency);
        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(variationName))
            summaryParts.Add($"Tamanho: {variationName}");

        var canonicalModifiers = new List<object>();
        var customIngredients = new List<CatalogQuotedCustomIngredient>();
        decimal totalCustomIngredientsCost = 0m;

        foreach (var groupGroup in resolvedSelections.GroupBy(s => s.Group.Id))
        {
            var grp = loadedGroups[groupGroup.Key];
            var rule = effectiveRules[groupGroup.Key];
            var remainingFreeUnits = rule.Free;
            var isFlavorWithStrategy = grp.SelectionKind == ModifierGroupKind.FlavorSelection && product.PricingStrategy != ProductPricingStrategy.FixedPlusAddons;

            foreach (var (_, option, qty) in groupGroup)
            {
                decimal chargedAmountForLine = 0m;
                if (!isFlavorWithStrategy)
                {
                    if (remainingFreeUnits > 0)
                    {
                        var freeUsed = Math.Min(remainingFreeUnits, qty);
                        remainingFreeUnits -= freeUsed;
                        var paidUnits = qty - freeUsed;
                        if (paidUnits > 0)
                            chargedAmountForLine = option.AdditionalPrice.Amount * paidUnits;
                    }
                    else
                    {
                        chargedAmountForLine = option.AdditionalPrice.Amount * qty;
                    }
                }

                if (chargedAmountForLine > 0m)
                    modifierExtra = modifierExtra.Add(Money.Create(chargedAmountForLine, basePrice.Currency).Value);

                var prefix = grp.SelectionKind == ModifierGroupKind.IngredientRemoval
                    ? "Sem "
                    : grp.SelectionKind == ModifierGroupKind.Substitution && !string.IsNullOrWhiteSpace(option.ReplacesLabel)
                        ? $"Troca ({option.ReplacesLabel} →) "
                        : $"{qty}x ";
                summaryParts.Add($"{prefix}{option.Name}");

                if (option.SupplyItemId.HasValue && option.ConsumptionQuantity > 0m)
                {
                    supplyCosts.TryGetValue(option.SupplyItemId.Value, out var sInfo);
                    var lossPct = option.LossPercent > 0m ? option.LossPercent : (sInfo?.DefaultLossPercent ?? 0m);
                    var grossQty = decimal.Round(option.ConsumptionQuantity * qty * (1m + (lossPct / 100m)), 4, MidpointRounding.AwayFromZero);
                    var unitCode = option.ConsumptionUnitCode ?? sInfo?.ContentUnitCode ?? "unidade";
                    var estCost = sInfo is not null ? decimal.Round(grossQty * sInfo.UnitCostInContentUnit, 4, MidpointRounding.AwayFromZero) : 0m;
                    totalCustomIngredientsCost += estCost;
                    customIngredients.Add(new CatalogQuotedCustomIngredient(option.SupplyItemId.Value, grossQty, unitCode, option.Name, estCost));
                }

                canonicalModifiers.Add(new
                {
                    groupId = grp.Id,
                    groupName = grp.Name,
                    kind = (int)grp.SelectionKind,
                    optionId = option.Id,
                    name = option.Name,
                    qty,
                    chargedAmount = chargedAmountForLine,
                    unitPrice = option.AdditionalPrice.Amount
                });
            }
        }

        var unitPrice = effectivePriceAfterPromo.Add(modifierExtra);
        var canonicalJson = JsonSerializer.Serialize(new
        {
            variationId = chosenVariation?.Id,
            modifiers = canonicalModifiers,
            customIngredients = customIngredients.Select(i => new { i.SupplyItemId, i.GrossQuantity, i.UnitCode, i.IngredientName, i.EstimatedCost })
        });
        var summaryText = summaryParts.Count == 0 ? "Padrão" : string.Join(" | ", summaryParts);

        return Result.Success(new CatalogProductQuote(
            product.Id,
            product.CategoryId,
            product.Name,
            chosenVariation?.Id,
            variationName,
            unitPrice,
            appliedPromo is not null && !appliedPromo.CanCombineWithCoupons,
            summaryText,
            canonicalJson,
            customIngredients,
            decimal.Round(totalCustomIngredientsCost, 2, MidpointRounding.AwayFromZero)));
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
