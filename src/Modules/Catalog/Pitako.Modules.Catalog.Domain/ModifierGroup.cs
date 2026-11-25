using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Catalog.Domain;

public enum ModifierGroupType
{
    Addon = 1,
    Complement = 2
}

public sealed class ModifierOption : Entity<Guid>
{
    private ModifierOption()
    {
        Name = string.Empty;
        AdditionalPrice = Money.Zero();
    }

    internal ModifierOption(
        Guid id,
        Guid tenantId,
        Guid modifierGroupId,
        string name,
        Money additionalPrice,
        int maxQuantity,
        int displayOrder) : base(id)
    {
        TenantId = tenantId;
        ModifierGroupId = modifierGroupId;
        Name = name;
        AdditionalPrice = additionalPrice;
        MaxQuantity = maxQuantity;
        DisplayOrder = displayOrder;
        IsAvailable = true;
    }

    public Guid TenantId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public string Name { get; private set; }
    public Money AdditionalPrice { get; private set; }
    public int MaxQuantity { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsAvailable { get; private set; }

    public void Pause() => IsAvailable = false;
    public void Reactivate() => IsAvailable = true;
}

/// <summary>
/// Grupo reutilizável de adicionais ou complementos vinculado a produtos do cardápio.
/// </summary>
public sealed class ModifierGroup : TenantScopedAggregateRoot
{
    private readonly List<ModifierOption> _options = new();

    private ModifierGroup()
    {
        Name = string.Empty;
    }

    private ModifierGroup(
        Guid id,
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        Type = type;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        IsRequired = isRequired;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public string Name { get; private set; }
    public ModifierGroupType Type { get; private set; }
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public bool IsRequired { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<ModifierOption> Options => _options.AsReadOnly();

    public static Result<ModifierGroup> Create(
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.NameRequired", "Nome do grupo é obrigatório."));

        if (minSelections < 0 || maxSelections < 1 || minSelections > maxSelections)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.InvalidRange", "Limites mínimo e máximo inválidos."));

        if (isRequired && minSelections < 1)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.RequiredMinOne", "Grupo obrigatório exige ao menos 1 seleção mínima."));

        return Result.Success(new ModifierGroup(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            type,
            minSelections,
            maxSelections,
            isRequired,
            displayOrder));
    }

    public Result<ModifierOption> AddOption(string name, Money additionalPrice, int maxQuantity = 1, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ModifierOption>(Error.Validation("ModifierOption.NameRequired", "Nome da opção é obrigatório."));

        if (maxQuantity <= 0)
            return Result.Failure<ModifierOption>(Error.Validation("ModifierOption.InvalidMaxQty", "Quantidade máxima deve ser positiva."));

        ArgumentNullException.ThrowIfNull(additionalPrice);

        var option = new ModifierOption(Guid.NewGuid(), TenantId, Id, name.Trim(), additionalPrice, maxQuantity, displayOrder);
        _options.Add(option);
        return Result.Success(option);
    }
}
