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

public enum ModifierGroupKind
{
    RequiredChoice = 1,
    FreeIncluded = 2,
    PaidAddon = 3,
    IngredientRemoval = 4,
    Substitution = 5,
    FlavorSelection = 6
}

public enum OptionCountingMode
{
    ByDistinctOption = 1,
    ByTotalQuantity = 2
}

public sealed class ModifierOption : Entity<Guid>
{
    private ModifierOption()
    {
        Name = string.Empty;
        AdditionalPrice = Money.Zero();
        CostPrice = Money.Zero();
    }

    internal ModifierOption(
        Guid id,
        Guid tenantId,
        Guid modifierGroupId,
        string name,
        Money additionalPrice,
        int maxQuantity,
        int displayOrder,
        string? description = null,
        Guid? supplyItemId = null,
        decimal consumptionQuantity = 0m,
        string? consumptionUnitCode = null,
        decimal lossPercent = 0m,
        string? replacesLabel = null,
        string? incompatibleOptionIdsCsv = null,
        Money? costPrice = null) : base(id)
    {
        TenantId = tenantId;
        ModifierGroupId = modifierGroupId;
        Name = name;
        AdditionalPrice = additionalPrice;
        CostPrice = costPrice ?? Money.Zero(additionalPrice.Currency);
        MaxQuantity = maxQuantity;
        DisplayOrder = displayOrder;
        Description = description;
        SupplyItemId = supplyItemId;
        ConsumptionQuantity = consumptionQuantity;
        ConsumptionUnitCode = consumptionUnitCode;
        LossPercent = lossPercent;
        ReplacesLabel = replacesLabel;
        IncompatibleOptionIdsCsv = incompatibleOptionIdsCsv;
        IsAvailable = true;
    }

    public Guid TenantId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public string Name { get; private set; }
    public Money AdditionalPrice { get; private set; }
    public Money CostPrice { get; private set; }
    public int MaxQuantity { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? Description { get; private set; }
    public Guid? SupplyItemId { get; private set; }
    public decimal ConsumptionQuantity { get; private set; }
    public string? ConsumptionUnitCode { get; private set; }
    public decimal LossPercent { get; private set; }
    public string? ReplacesLabel { get; private set; }
    public string? IncompatibleOptionIdsCsv { get; private set; }

    public Result Update(
        string name,
        Money additionalPrice,
        int maxQuantity,
        int displayOrder,
        string? description = null,
        Guid? supplyItemId = null,
        decimal consumptionQuantity = 0m,
        string? consumptionUnitCode = null,
        decimal lossPercent = 0m,
        string? replacesLabel = null,
        string? incompatibleOptionIdsCsv = null,
        Money? costPrice = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("ModifierOption.NameRequired", "Nome da opção é obrigatório."));
        if (maxQuantity <= 0)
            return Result.Failure(Error.Validation("ModifierOption.InvalidMaxQty", "Quantidade máxima deve ser positiva."));
        if (consumptionQuantity < 0m || lossPercent < 0m || lossPercent >= 100m)
            return Result.Failure(Error.Validation("ModifierOption.InvalidConsumption", "Consumo e percentual de perda devem ser válidos (0 a 99%)."));

        ArgumentNullException.ThrowIfNull(additionalPrice);

        Name = name.Trim();
        AdditionalPrice = additionalPrice;
        if (costPrice is not null) CostPrice = costPrice;
        MaxQuantity = maxQuantity;
        DisplayOrder = displayOrder;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SupplyItemId = supplyItemId == Guid.Empty ? null : supplyItemId;
        ConsumptionQuantity = consumptionQuantity;
        ConsumptionUnitCode = string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant();
        LossPercent = lossPercent;
        ReplacesLabel = string.IsNullOrWhiteSpace(replacesLabel) ? null : replacesLabel.Trim();
        IncompatibleOptionIdsCsv = string.IsNullOrWhiteSpace(incompatibleOptionIdsCsv) ? null : incompatibleOptionIdsCsv.Trim();
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsAvailable = false;
    public void Reactivate() => IsAvailable = true;
}

/// <summary>
/// Grupo reutilizável de opções, adicionais, complementos, remoções, substituições e sabores vinculado a produtos.
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
        int displayOrder,
        ModifierGroupKind selectionKind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string? categoryTag) : base(id, tenantId)
    {
        Name = name;
        Type = type;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        IsRequired = isRequired;
        DisplayOrder = displayOrder;
        SelectionKind = selectionKind;
        FreeAllowance = freeAllowance;
        CountingMode = countingMode;
        CategoryTag = categoryTag;
        IsActive = true;
    }

    public string Name { get; private set; }
    public ModifierGroupType Type { get; private set; }
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public bool IsRequired { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public ModifierGroupKind SelectionKind { get; private set; }
    public int FreeAllowance { get; private set; }
    public OptionCountingMode CountingMode { get; private set; }
    public string? CategoryTag { get; private set; }

    public IReadOnlyCollection<ModifierOption> Options => _options.AsReadOnly();

    public static Result<ModifierGroup> Create(
        Guid tenantId,
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder = 0,
        ModifierGroupKind? selectionKind = null,
        int freeAllowance = 0,
        OptionCountingMode countingMode = OptionCountingMode.ByTotalQuantity,
        string? categoryTag = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.NameRequired", "Nome do grupo é obrigatório."));

        if (minSelections < 0 || maxSelections < 1 || minSelections > maxSelections || freeAllowance < 0)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.InvalidRange", "Limites mínimo, máximo e gratuito inválidos."));

        if (isRequired && minSelections < 1)
            return Result.Failure<ModifierGroup>(Error.Validation("ModifierGroup.RequiredMinOne", "Grupo obrigatório exige ao menos 1 seleção mínima."));

        var resolvedKind = selectionKind ?? (isRequired
            ? ModifierGroupKind.RequiredChoice
            : type == ModifierGroupType.Complement ? ModifierGroupKind.FreeIncluded : ModifierGroupKind.PaidAddon);

        return Result.Success(new ModifierGroup(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            type,
            minSelections,
            maxSelections,
            isRequired,
            displayOrder,
            resolvedKind,
            freeAllowance,
            countingMode,
            string.IsNullOrWhiteSpace(categoryTag) ? null : categoryTag.Trim().ToLowerInvariant()));
    }

    public Result UpdateConfiguration(
        string name,
        ModifierGroupType type,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int displayOrder,
        ModifierGroupKind selectionKind,
        int freeAllowance,
        OptionCountingMode countingMode,
        string? categoryTag)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("ModifierGroup.NameRequired", "Nome do grupo é obrigatório."));

        if (minSelections < 0 || maxSelections < 1 || minSelections > maxSelections || freeAllowance < 0)
            return Result.Failure(Error.Validation("ModifierGroup.InvalidRange", "Limites mínimo, máximo e gratuito inválidos."));

        if (isRequired && minSelections < 1)
            return Result.Failure(Error.Validation("ModifierGroup.RequiredMinOne", "Grupo obrigatório exige ao menos 1 seleção mínima."));

        Name = name.Trim();
        Type = type;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        IsRequired = isRequired;
        DisplayOrder = displayOrder;
        SelectionKind = selectionKind;
        FreeAllowance = freeAllowance;
        CountingMode = countingMode;
        CategoryTag = string.IsNullOrWhiteSpace(categoryTag) ? null : categoryTag.Trim().ToLowerInvariant();
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;

    public Result<ModifierOption> AddOption(
        string name,
        Money additionalPrice,
        int maxQuantity = 1,
        int displayOrder = 0,
        string? description = null,
        Guid? supplyItemId = null,
        decimal consumptionQuantity = 0m,
        string? consumptionUnitCode = null,
        decimal lossPercent = 0m,
        string? replacesLabel = null,
        string? incompatibleOptionIdsCsv = null,
        Money? costPrice = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ModifierOption>(Error.Validation("ModifierOption.NameRequired", "Nome da opção é obrigatório."));

        if (maxQuantity <= 0)
            return Result.Failure<ModifierOption>(Error.Validation("ModifierOption.InvalidMaxQty", "Quantidade máxima deve ser positiva."));

        if (consumptionQuantity < 0m || lossPercent < 0m || lossPercent >= 100m)
            return Result.Failure<ModifierOption>(Error.Validation("ModifierOption.InvalidConsumption", "Consumo e percentual de perda devem ser válidos (0 a 99%)."));

        ArgumentNullException.ThrowIfNull(additionalPrice);

        var option = new ModifierOption(
            Guid.NewGuid(),
            TenantId,
            Id,
            name.Trim(),
            additionalPrice,
            maxQuantity,
            displayOrder,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            supplyItemId == Guid.Empty ? null : supplyItemId,
            consumptionQuantity,
            string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant(),
            lossPercent,
            string.IsNullOrWhiteSpace(replacesLabel) ? null : replacesLabel.Trim(),
            string.IsNullOrWhiteSpace(incompatibleOptionIdsCsv) ? null : incompatibleOptionIdsCsv.Trim(),
            costPrice);
        _options.Add(option);
        return Result.Success(option);
    }

    public Result RemoveOption(Guid optionId)
    {
        var option = _options.FirstOrDefault(o => o.Id == optionId);
        if (option is null)
            return Result.Failure(Error.NotFound("ModifierOption.NotFound", "Opção não encontrada."));

        _options.Remove(option);
        return Result.Success();
    }
}
