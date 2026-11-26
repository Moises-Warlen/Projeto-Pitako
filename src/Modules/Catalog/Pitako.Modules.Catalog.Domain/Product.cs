using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Domain;

public enum ProductSaleMode
{
    Unit = 1,
    Weight = 2,
    CustomizableAcai = 3
}

public enum ProductPricingStrategy
{
    FixedPlusAddons = 1,
    FlavorHighestPrice = 2,
    FlavorAveragePrice = 3,
    FlavorProportional = 4
}

public enum DeletionOutcome
{
    PhysicallyDeleted = 1,
    ArchivedPreservingHistory = 2
}

public sealed class ProductImage : Entity<Guid>
{
    private ProductImage()
    {
        ImageUrl = string.Empty;
    }

    internal ProductImage(Guid id, Guid tenantId, Guid productId, string imageUrl, int displayOrder, bool isPrimary)
        : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        ImageUrl = imageUrl;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ImageUrl { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

public sealed class ProductVariation : Entity<Guid>
{
    private ProductVariation()
    {
        Name = string.Empty;
        Price = Money.Zero();
    }

    internal ProductVariation(
        Guid id,
        Guid tenantId,
        Guid productId,
        string name,
        string? sku,
        Money price,
        int displayOrder,
        string? description = null,
        decimal? measureAmount = null,
        string? measureUnit = null,
        int maxFlavors = 1) : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        Name = name;
        Sku = sku;
        Price = price;
        DisplayOrder = displayOrder;
        Description = description;
        MeasureAmount = measureAmount;
        MeasureUnit = measureUnit;
        MaxFlavors = maxFlavors < 1 ? 1 : maxFlavors;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; }
    public string? Sku { get; private set; }
    public Money Price { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public string? Description { get; private set; }
    public decimal? MeasureAmount { get; private set; }
    public string? MeasureUnit { get; private set; }
    public int MaxFlavors { get; private set; }

    public Result UpdateDetails(
        string name,
        Money price,
        string? sku = null,
        int displayOrder = 0,
        string? description = null,
        decimal? measureAmount = null,
        string? measureUnit = null,
        int maxFlavors = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Product.VariationNameRequired", "Nome da variação é obrigatório."));
        ArgumentNullException.ThrowIfNull(price);

        Name = name.Trim();
        Price = price;
        Sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant();
        DisplayOrder = displayOrder;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MeasureAmount = measureAmount;
        MeasureUnit = string.IsNullOrWhiteSpace(measureUnit) ? null : measureUnit.Trim().ToLowerInvariant();
        MaxFlavors = maxFlavors < 1 ? 1 : maxFlavors;
        return Result.Success();
    }

    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}

public sealed class ProductModifierLink : Entity<Guid>
{
    private ProductModifierLink()
    {
    }

    internal ProductModifierLink(Guid id, Guid tenantId, Guid productId, Guid modifierGroupId, int displayOrder)
        : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        ModifierGroupId = modifierGroupId;
        DisplayOrder = displayOrder;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public int DisplayOrder { get; private set; }
}

/// <summary>
/// Sobrescreve limites (mínimo, máximo, cota gratuita, obrigatoriedade ou ocultação)
/// de um grupo de opções especificamente por produto e variação/tamanho.
/// </summary>
public sealed class VariationModifierRule : Entity<Guid>
{
    private VariationModifierRule()
    {
    }

    internal VariationModifierRule(
        Guid id,
        Guid tenantId,
        Guid productId,
        Guid? productVariationId,
        Guid modifierGroupId,
        int minSelections,
        int maxSelections,
        int freeAllowance,
        bool isRequired,
        bool isHidden) : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        ProductVariationId = productVariationId == Guid.Empty ? null : productVariationId;
        ModifierGroupId = modifierGroupId;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        FreeAllowance = freeAllowance;
        IsRequired = isRequired;
        IsHidden = isHidden;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductVariationId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public int FreeAllowance { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsHidden { get; private set; }

    internal void UpdateRule(int minSelections, int maxSelections, int freeAllowance, bool isRequired, bool isHidden)
    {
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        FreeAllowance = freeAllowance;
        IsRequired = isRequired;
        IsHidden = isHidden;
    }
}

/// <summary>
/// Agregado raiz de produto do catálogo com variações, galeria de fotos com imagem principal,
/// grupos de adicionais/complementos, destaque, pausa e preservação de histórico (Seção 12).
/// </summary>
public sealed class Product : TenantScopedAggregateRoot
{
    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariation> _variations = new();
    private readonly List<ProductModifierLink> _modifierLinks = new();
    private readonly List<VariationModifierRule> _variationRules = new();

    private Product()
    {
        Name = string.Empty;
        BasePrice = Money.Zero();
    }

    private Product(
        Guid id,
        Guid tenantId,
        Guid categoryId,
        string name,
        string? description,
        string? sku,
        Money basePrice,
        ProductSaleMode saleMode,
        bool isFeatured,
        int displayOrder,
        bool isNew = false,
        bool isRecommended = false,
        ProductPricingStrategy pricingStrategy = ProductPricingStrategy.FixedPlusAddons,
        string? tagsCsv = null,
        int? preparationMinutes = null,
        string? customTemplateKey = null,
        bool allowsNotes = true) : base(id, tenantId)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        Sku = sku;
        BasePrice = basePrice;
        SaleMode = saleMode;
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
        IsNew = isNew;
        IsRecommended = isRecommended;
        PricingStrategy = pricingStrategy;
        TagsCsv = tagsCsv;
        PreparationMinutes = preparationMinutes;
        CustomTemplateKey = customTemplateKey;
        AllowsNotes = allowsNotes;
        Status = CatalogItemStatus.Active;
    }

    public Guid CategoryId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? Sku { get; private set; }
    public Money BasePrice { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public CatalogItemStatus Status { get; private set; }
    public bool IsFeatured { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsNew { get; private set; }
    public bool IsRecommended { get; private set; }
    public ProductPricingStrategy PricingStrategy { get; private set; }
    public string? TagsCsv { get; private set; }
    public int? PreparationMinutes { get; private set; }
    public string? CustomTemplateKey { get; private set; }
    public bool AllowsNotes { get; private set; }

    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyCollection<ProductVariation> Variations => _variations.AsReadOnly();
    public IReadOnlyCollection<ProductModifierLink> ModifierLinks => _modifierLinks.AsReadOnly();
    public IReadOnlyCollection<VariationModifierRule> VariationRules => _variationRules.AsReadOnly();

    public static Result<Product> Create(
        Guid tenantId,
        Guid categoryId,
        string name,
        Money basePrice,
        string? description = null,
        string? sku = null,
        ProductSaleMode saleMode = ProductSaleMode.Unit,
        bool isFeatured = false,
        int displayOrder = 0,
        bool isNew = false,
        bool isRecommended = false,
        ProductPricingStrategy pricingStrategy = ProductPricingStrategy.FixedPlusAddons,
        string? tagsCsv = null,
        int? preparationMinutes = null,
        string? customTemplateKey = null,
        bool allowsNotes = true)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Product>(Error.Validation("Product.TenantRequired", "TenantId é obrigatório."));

        if (categoryId == Guid.Empty)
            return Result.Failure<Product>(Error.Validation("Product.CategoryRequired", "Categoria é obrigatória."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Product>(Error.Validation("Product.NameRequired", "Nome do produto é obrigatório."));

        ArgumentNullException.ThrowIfNull(basePrice);

        return Result.Success(new Product(
            Guid.NewGuid(),
            tenantId,
            categoryId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant(),
            basePrice,
            saleMode,
            isFeatured,
            displayOrder,
            isNew,
            isRecommended,
            pricingStrategy,
            string.IsNullOrWhiteSpace(tagsCsv) ? null : tagsCsv.Trim(),
            preparationMinutes,
            string.IsNullOrWhiteSpace(customTemplateKey) ? null : customTemplateKey.Trim().ToLowerInvariant(),
            allowsNotes));
    }

    public Result UpdateDetails(
        Guid categoryId,
        string name,
        Money basePrice,
        string? description,
        string? sku,
        bool isFeatured,
        int displayOrder,
        ProductSaleMode? saleMode = null,
        bool? isNew = null,
        bool? isRecommended = null,
        ProductPricingStrategy? pricingStrategy = null,
        string? tagsCsv = null,
        int? preparationMinutes = null,
        string? customTemplateKey = null,
        bool? allowsNotes = null)
    {
        if (categoryId == Guid.Empty)
            return Result.Failure(Error.Validation("Product.CategoryRequired", "Categoria é obrigatória."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Product.NameRequired", "Nome do produto é obrigatório."));

        ArgumentNullException.ThrowIfNull(basePrice);

        CategoryId = categoryId;
        Name = name.Trim();
        BasePrice = basePrice;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant();
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
        if (saleMode.HasValue) SaleMode = saleMode.Value;
        if (isNew.HasValue) IsNew = isNew.Value;
        if (isRecommended.HasValue) IsRecommended = isRecommended.Value;
        if (pricingStrategy.HasValue) PricingStrategy = pricingStrategy.Value;
        if (tagsCsv is not null) TagsCsv = string.IsNullOrWhiteSpace(tagsCsv) ? null : tagsCsv.Trim();
        if (preparationMinutes.HasValue) PreparationMinutes = preparationMinutes;
        if (customTemplateKey is not null) CustomTemplateKey = string.IsNullOrWhiteSpace(customTemplateKey) ? null : customTemplateKey.Trim().ToLowerInvariant();
        if (allowsNotes.HasValue) AllowsNotes = allowsNotes.Value;
        return Result.Success();
    }

    public Result<ProductImage> AddImage(string imageUrl, int displayOrder = 0, bool isPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return Result.Failure<ProductImage>(Error.Validation("Product.ImageUrlRequired", "URL da imagem é obrigatória."));

        var shouldBePrimary = isPrimary || _images.Count == 0;
        if (shouldBePrimary)
        {
            foreach (var img in _images)
                img.SetPrimary(false);
        }

        var image = new ProductImage(Guid.NewGuid(), TenantId, Id, imageUrl.Trim(), displayOrder, shouldBePrimary);
        _images.Add(image);
        return Result.Success(image);
    }

    public Result SetPrimaryImage(Guid imageId)
    {
        var target = _images.FirstOrDefault(i => i.Id == imageId);
        if (target is null)
            return Result.Failure(Error.NotFound("Product.ImageNotFound", "Imagem não encontrada no produto."));

        foreach (var img in _images)
            img.SetPrimary(img.Id == imageId);

        return Result.Success();
    }

    public Result RemoveImage(Guid imageId)
    {
        var img = _images.FirstOrDefault(i => i.Id == imageId);
        if (img is null)
            return Result.Failure(Error.NotFound("Product.ImageNotFound", "Imagem não encontrada."));

        var wasPrimary = img.IsPrimary;
        _images.Remove(img);
        if (wasPrimary && _images.Count > 0)
            _images.OrderBy(i => i.DisplayOrder).First().SetPrimary(true);
        return Result.Success();
    }

    public Result<ProductVariation> AddVariation(
        string name,
        Money price,
        string? sku = null,
        int displayOrder = 0,
        string? description = null,
        decimal? measureAmount = null,
        string? measureUnit = null,
        int maxFlavors = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ProductVariation>(Error.Validation("Product.VariationNameRequired", "Nome da variação é obrigatório."));

        ArgumentNullException.ThrowIfNull(price);

        var variation = new ProductVariation(
            Guid.NewGuid(),
            TenantId,
            Id,
            name.Trim(),
            string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant(),
            price,
            displayOrder,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            measureAmount,
            string.IsNullOrWhiteSpace(measureUnit) ? null : measureUnit.Trim().ToLowerInvariant(),
            maxFlavors);

        _variations.Add(variation);
        return Result.Success(variation);
    }

    public Result RemoveVariation(Guid variationId)
    {
        var v = _variations.FirstOrDefault(x => x.Id == variationId);
        if (v is null)
            return Result.Failure(Error.NotFound("Product.VariationNotFound", "Variação não encontrada."));

        _variations.Remove(v);
        _variationRules.RemoveAll(r => r.ProductVariationId == variationId);
        return Result.Success();
    }

    public void AttachModifierGroup(Guid modifierGroupId, int displayOrder = 0)
    {
        if (modifierGroupId == Guid.Empty || _modifierLinks.Any(l => l.ModifierGroupId == modifierGroupId))
            return;

        _modifierLinks.Add(new ProductModifierLink(Guid.NewGuid(), TenantId, Id, modifierGroupId, displayOrder));
    }

    public void DetachModifierGroup(Guid modifierGroupId)
    {
        _modifierLinks.RemoveAll(l => l.ModifierGroupId == modifierGroupId);
        _variationRules.RemoveAll(r => r.ModifierGroupId == modifierGroupId);
    }

    public Result<VariationModifierRule> SetVariationRule(
        Guid? variationId,
        Guid modifierGroupId,
        int minSelections,
        int maxSelections,
        int freeAllowance,
        bool isRequired,
        bool isHidden = false)
    {
        if (modifierGroupId == Guid.Empty)
            return Result.Failure<VariationModifierRule>(Error.Validation("Product.ModifierGroupRequired", "Grupo de opções é obrigatório."));
        if (minSelections < 0 || maxSelections < 0 || freeAllowance < 0 || (maxSelections > 0 && minSelections > maxSelections))
            return Result.Failure<VariationModifierRule>(Error.Validation("Product.InvalidRuleRange", "Limites mínimo, máximo e gratuito inválidos."));

        var targetVariationId = variationId == Guid.Empty ? null : variationId;
        var existing = _variationRules.FirstOrDefault(r => r.ProductVariationId == targetVariationId && r.ModifierGroupId == modifierGroupId);
        if (existing is not null)
        {
            existing.UpdateRule(minSelections, maxSelections, freeAllowance, isRequired, isHidden);
            return Result.Success(existing);
        }

        var rule = new VariationModifierRule(
            Guid.NewGuid(),
            TenantId,
            Id,
            targetVariationId,
            modifierGroupId,
            minSelections,
            maxSelections,
            freeAllowance,
            isRequired,
            isHidden);
        _variationRules.Add(rule);
        return Result.Success(rule);
    }

    public void RemoveVariationRule(Guid ruleId) =>
        _variationRules.RemoveAll(r => r.Id == ruleId);

    public void Pause() => Status = CatalogItemStatus.Paused;

    public Result Reactivate()
    {
        if (Status == CatalogItemStatus.Archived)
            return Result.Failure(Error.Conflict("Product.Archived", "Produto arquivado para preservação de histórico não deve ser reativado."));

        Status = CatalogItemStatus.Active;
        return Result.Success();
    }

    public void SetFeatured(bool isFeatured) => IsFeatured = isFeatured;

    /// <summary>
    /// Produtos pausados ou arquivados não podem receber novos pedidos (Seção 12).
    /// </summary>
    public bool CanReceiveOrders() => Status == CatalogItemStatus.Active;

    /// <summary>
    /// Aplica regra de exclusão preservando registros históricos (Seção 12).
    /// </summary>
    public DeletionOutcome PrepareDeletion(bool hasHistoricalReferences)
    {
        if (hasHistoricalReferences)
        {
            Status = CatalogItemStatus.Archived;
            IsFeatured = false;
            return DeletionOutcome.ArchivedPreservingHistory;
        }

        return DeletionOutcome.PhysicallyDeleted;
    }
}
