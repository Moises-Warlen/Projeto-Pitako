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
        int displayOrder) : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        Name = name;
        Sku = sku;
        Price = price;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; }
    public string? Sku { get; private set; }
    public Money Price { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

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
/// Agregado raiz de produto do catálogo com variações, galeria de fotos com imagem principal,
/// grupos de adicionais/complementos, destaque, pausa e preservação de histórico (Seção 12).
/// </summary>
public sealed class Product : TenantScopedAggregateRoot
{
    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariation> _variations = new();
    private readonly List<ProductModifierLink> _modifierLinks = new();

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
        int displayOrder) : base(id, tenantId)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        Sku = sku;
        BasePrice = basePrice;
        SaleMode = saleMode;
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
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

    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyCollection<ProductVariation> Variations => _variations.AsReadOnly();
    public IReadOnlyCollection<ProductModifierLink> ModifierLinks => _modifierLinks.AsReadOnly();

    public static Result<Product> Create(
        Guid tenantId,
        Guid categoryId,
        string name,
        Money basePrice,
        string? description = null,
        string? sku = null,
        ProductSaleMode saleMode = ProductSaleMode.Unit,
        bool isFeatured = false,
        int displayOrder = 0)
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
            displayOrder));
    }

    public Result UpdateDetails(
        Guid categoryId,
        string name,
        Money basePrice,
        string? description,
        string? sku,
        bool isFeatured,
        int displayOrder)
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

    public Result<ProductVariation> AddVariation(string name, Money price, string? sku = null, int displayOrder = 0)
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
            displayOrder);

        _variations.Add(variation);
        return Result.Success(variation);
    }

    public void AttachModifierGroup(Guid modifierGroupId, int displayOrder = 0)
    {
        if (modifierGroupId == Guid.Empty || _modifierLinks.Any(l => l.ModifierGroupId == modifierGroupId))
            return;

        _modifierLinks.Add(new ProductModifierLink(Guid.NewGuid(), TenantId, Id, modifierGroupId, displayOrder));
    }

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
