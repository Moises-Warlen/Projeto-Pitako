using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Catalog.Domain;

/// <summary>
/// Situação operacional de itens e categorias do cardápio (Seção 12).
/// Itens pausados ou arquivados não podem receber novos pedidos, preservando histórico.
/// </summary>
public enum CatalogItemStatus
{
    Active = 1,
    Paused = 2,
    Archived = 3
}

/// <summary>
/// Categoria ordenável de produtos pertencente exclusivamente a um estabelecimento.
/// </summary>
public sealed class Category : TenantScopedAggregateRoot
{
    private Category()
    {
        Name = string.Empty;
    }

    private Category(
        Guid id,
        Guid tenantId,
        string name,
        string? description,
        string? imageUrl,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        DisplayOrder = displayOrder;
        Status = CatalogItemStatus.Active;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public int DisplayOrder { get; private set; }
    public CatalogItemStatus Status { get; private set; }

    public static Result<Category> Create(
        Guid tenantId,
        string name,
        string? description = null,
        string? imageUrl = null,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Category>(Error.Validation("Category.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(Error.Validation("Category.NameRequired", "Nome da categoria é obrigatório."));

        return Result.Success(new Category(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim(),
            displayOrder));
    }

    public Result Update(string name, string? description, string? imageUrl, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Category.NameRequired", "Nome da categoria é obrigatório."));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        DisplayOrder = displayOrder;
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => Status = CatalogItemStatus.Paused;
    public void Reactivate() => Status = CatalogItemStatus.Active;
    public void Archive() => Status = CatalogItemStatus.Archived;
}
