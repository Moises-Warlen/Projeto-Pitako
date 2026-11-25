using Pitako.Modules.Catalog.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Application.Abstractions;

public sealed record CatalogModifierSelection(Guid ModifierGroupId, Guid ModifierOptionId, int Quantity = 1);

public sealed record CatalogProductQuote(
    Guid ProductId,
    Guid CategoryId,
    string ProductName,
    Guid? VariationId,
    string? VariationName,
    Money UnitPrice,
    bool HasNonCombinablePromotion,
    string CustomizationSummaryText,
    string CanonicalSelectionsJson);

/// <summary>
/// Contrato público para cotação autoritativa de produtos, variações, adicionais e promoções
/// pelo Carrinho e Pedidos (Fases 11 e 13).
/// </summary>
public interface ICatalogItemPricingService
{
    Task<Result<CatalogProductQuote>> QuoteProductItemAsync(
        Guid productId,
        Guid? variationId,
        IReadOnlyList<CatalogModifierSelection>? modifiers,
        CancellationToken cancellationToken = default);
}

public interface ICategoryRepository
{
    Task<Category?> FindByIdAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> ListOrderedAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IProductRepository
{
    Task<Product?> FindByIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> ListByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> ListAllOrderedAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    void Remove(Product product);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IModifierGroupRepository
{
    Task<ModifierGroup?> FindByIdAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModifierGroup>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ModifierGroup group, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IComboRepository
{
    Task<Combo?> FindByIdAsync(Guid comboId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Combo>> ListAllOrderedAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Combo combo, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPromotionRepository
{
    Task<Promotion?> FindByIdAsync(Guid promotionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Promotion>> ListActiveCandidatesAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifica se um produto já participa de pedidos ou registros históricos para aplicar exclusão segura (Seção 12).
/// </summary>
public interface IProductHistoryReferenceChecker
{
    Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);
}
