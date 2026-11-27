using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.AcaiBuilder.Domain;

/// <summary>
/// Modo de contabilização dos acompanhamentos gratuitos no copo de açaí (Seção 13).
/// </summary>
public enum AcaiFreeCountingMode
{
    /// <summary>Cada acompanhamento distinto conta 1 no limite gratuito.</summary>
    ByDistinctItem = 1,

    /// <summary>Cada porção selecionada soma no limite gratuito (permite múltiplas porções do mesmo item até o teto).</summary>
    ByPortion = 2
}

/// <summary>
/// Tipo de categoria de adicionais no montador de açaí (Grátis, Pago, Frutas, Cremes, Complementos).
/// </summary>
public enum AcaiAddonCategoryKind
{
    FreeIncluded = 1,
    PaidAddon = 2,
    Fruits = 3,
    CreamsAndToppings = 4,
    DryComplements = 5
}

/// <summary>
/// Categoria organizacional para adicionais gratuitos, pagos e frutas do açaí.
/// </summary>
public sealed class AcaiAddonCategory : TenantScopedAggregateRoot
{
    private AcaiAddonCategory()
    {
        Name = string.Empty;
    }

    private AcaiAddonCategory(
        Guid id,
        Guid tenantId,
        string name,
        AcaiAddonCategoryKind kind,
        bool isFreeByDefault,
        string? description,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        Kind = kind;
        IsFreeByDefault = isFreeByDefault;
        Description = description;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public string Name { get; private set; }
    public AcaiAddonCategoryKind Kind { get; private set; }
    public bool IsFreeByDefault { get; private set; }
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<AcaiAddonCategory> Create(
        Guid tenantId,
        string name,
        AcaiAddonCategoryKind kind = AcaiAddonCategoryKind.FreeIncluded,
        bool isFreeByDefault = true,
        string? description = null,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AcaiAddonCategory>(Error.Validation("Acai.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<AcaiAddonCategory>(Error.Validation("Acai.CategoryNameRequired", "Nome da categoria de adicionais é obrigatório."));

        return Result.Success(new AcaiAddonCategory(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            kind,
            isFreeByDefault,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            displayOrder));
    }

    public Result Update(
        string name,
        AcaiAddonCategoryKind kind,
        bool isFreeByDefault,
        string? description = null,
        int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Acai.CategoryNameRequired", "Nome da categoria de adicionais é obrigatório."));

        Name = name.Trim();
        Kind = kind;
        IsFreeByDefault = isFreeByDefault;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DisplayOrder = displayOrder;
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Tamanho configurável de copo/recipiente de açaí com capacidade em ml, preço base,
/// preço de custo, descrição e regras de adicionais gratuitos/pagos (Seção 13).
/// </summary>
public sealed class AcaiCupSize : TenantScopedAggregateRoot
{
    private AcaiCupSize()
    {
        Name = string.Empty;
        BasePrice = Money.Zero();
        CostPrice = Money.Zero();
        AllowFreeToppings = true;
        AllowPaidAddons = true;
    }

    private AcaiCupSize(
        Guid id,
        Guid tenantId,
        string name,
        int volumeMl,
        Money basePrice,
        int freeToppingsLimit,
        int minFreeToppings,
        AcaiFreeCountingMode countingMode,
        int? maxPaidAddonsPerCup,
        Guid? linkedProductId,
        string? sku,
        int displayOrder,
        string? description,
        Money costPrice,
        bool allowFreeToppings,
        bool allowPaidAddons) : base(id, tenantId)
    {
        Name = name;
        VolumeMl = volumeMl;
        BasePrice = basePrice;
        FreeToppingsLimit = freeToppingsLimit;
        MinFreeToppings = minFreeToppings;
        CountingMode = countingMode;
        MaxPaidAddonsPerCup = maxPaidAddonsPerCup;
        LinkedProductId = linkedProductId;
        Sku = sku;
        DisplayOrder = displayOrder;
        Description = description;
        CostPrice = costPrice;
        AllowFreeToppings = allowFreeToppings;
        AllowPaidAddons = allowPaidAddons;
        IsActive = true;
    }

    public string Name { get; private set; }
    public int VolumeMl { get; private set; }
    public Money BasePrice { get; private set; }
    public Money CostPrice { get; private set; }
    public string? Description { get; private set; }
    public int FreeToppingsLimit { get; private set; }
    public int MinFreeToppings { get; private set; }
    public AcaiFreeCountingMode CountingMode { get; private set; }
    public bool AllowFreeToppings { get; private set; }
    public bool AllowPaidAddons { get; private set; }
    public int? MaxPaidAddonsPerCup { get; private set; }
    public Guid? LinkedProductId { get; private set; }
    public string? Sku { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<AcaiCupSize> Create(
        Guid tenantId,
        string name,
        int volumeMl,
        Money basePrice,
        int freeToppingsLimit,
        int minFreeToppings = 0,
        AcaiFreeCountingMode countingMode = AcaiFreeCountingMode.ByPortion,
        int? maxPaidAddonsPerCup = null,
        Guid? linkedProductId = null,
        string? sku = null,
        int displayOrder = 0,
        string? description = null,
        Money? costPrice = null,
        bool allowFreeToppings = true,
        bool allowPaidAddons = true)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.CupNameRequired", "Nome do tamanho do copo é obrigatório."));

        if (volumeMl <= 0)
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.InvalidVolume", "Capacidade em ml deve ser positiva."));

        if (freeToppingsLimit < 0)
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.NegativeFreeLimit", "Limite de acompanhamentos gratuitos não pode ser negativo."));

        if (minFreeToppings < 0 || minFreeToppings > freeToppingsLimit)
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.InvalidMinFree", "Mínimo de acompanhamentos deve estar entre 0 e o limite gratuito."));

        if (maxPaidAddonsPerCup.HasValue && maxPaidAddonsPerCup.Value < 0)
            return Result.Failure<AcaiCupSize>(Error.Validation("Acai.InvalidMaxPaid", "Limite máximo de adicionais pagos não pode ser negativo."));

        ArgumentNullException.ThrowIfNull(basePrice);

        return Result.Success(new AcaiCupSize(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            volumeMl,
            basePrice,
            freeToppingsLimit,
            minFreeToppings,
            countingMode,
            maxPaidAddonsPerCup,
            linkedProductId == Guid.Empty ? null : linkedProductId,
            string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant(),
            displayOrder,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            costPrice ?? Money.Zero(basePrice.Currency),
            allowFreeToppings,
            allowPaidAddons));
    }

    public Result UpdateConfiguration(
        string name,
        int volumeMl,
        Money basePrice,
        int freeToppingsLimit,
        int minFreeToppings,
        AcaiFreeCountingMode countingMode,
        int? maxPaidAddonsPerCup,
        int displayOrder,
        string? description = null,
        Money? costPrice = null,
        bool? allowFreeToppings = null,
        bool? allowPaidAddons = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Acai.CupNameRequired", "Nome do tamanho do copo é obrigatório."));
        if (volumeMl <= 0)
            return Result.Failure(Error.Validation("Acai.InvalidVolume", "Capacidade em ml deve ser positiva."));
        if (freeToppingsLimit < 0 || minFreeToppings < 0 || minFreeToppings > freeToppingsLimit)
            return Result.Failure(Error.Validation("Acai.InvalidLimits", "Limites gratuitos inválidos para o copo."));
        if (maxPaidAddonsPerCup.HasValue && maxPaidAddonsPerCup.Value < 0)
            return Result.Failure(Error.Validation("Acai.InvalidMaxPaid", "Limite de adicionais pagos não pode ser negativo."));

        ArgumentNullException.ThrowIfNull(basePrice);

        Name = name.Trim();
        VolumeMl = volumeMl;
        BasePrice = basePrice;
        FreeToppingsLimit = freeToppingsLimit;
        MinFreeToppings = minFreeToppings;
        CountingMode = countingMode;
        MaxPaidAddonsPerCup = maxPaidAddonsPerCup;
        DisplayOrder = displayOrder;
        if (description is not null) Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (costPrice is not null) CostPrice = costPrice;
        if (allowFreeToppings.HasValue) AllowFreeToppings = allowFreeToppings.Value;
        if (allowPaidAddons.HasValue) AllowPaidAddons = allowPaidAddons.Value;
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Sabor/creme base configurável pela açaíteria (ex.: Açaí Tradicional, Zero, Cupuaçu).
/// </summary>
public sealed class AcaiBaseFlavor : TenantScopedAggregateRoot
{
    private AcaiBaseFlavor()
    {
        Name = string.Empty;
        ExtraPrice = Money.Zero();
    }

    private AcaiBaseFlavor(
        Guid id,
        Guid tenantId,
        string name,
        Money extraPrice,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        ExtraPrice = extraPrice;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public string Name { get; private set; }
    public Money ExtraPrice { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<AcaiBaseFlavor> Create(
        Guid tenantId,
        string name,
        Money? extraPrice = null,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AcaiBaseFlavor>(Error.Validation("Acai.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<AcaiBaseFlavor>(Error.Validation("Acai.FlavorNameRequired", "Nome do sabor base é obrigatório."));

        return Result.Success(new AcaiBaseFlavor(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            extraPrice ?? Money.Zero(),
            displayOrder));
    }

    public Result Update(string name, Money? extraPrice = null, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Acai.FlavorNameRequired", "Nome do sabor base é obrigatório."));

        Name = name.Trim();
        ExtraPrice = extraPrice ?? Money.Zero();
        DisplayOrder = displayOrder;
        return Result.Success();
    }

    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Acompanhamento gratuito configurável do módulo de açaí (área separada dos adicionais pagos, Seção 13).
/// </summary>
public sealed class AcaiFreeTopping : TenantScopedAggregateRoot
{
    private AcaiFreeTopping()
    {
        Name = string.Empty;
        CostPrice = Money.Zero();
    }

    private AcaiFreeTopping(
        Guid id,
        Guid tenantId,
        string name,
        int maxPortionsPerCup,
        Guid? supplyItemId,
        decimal? consumptionQuantityPerPortion,
        string? consumptionUnitCode,
        int displayOrder,
        string? description,
        Money costPrice,
        Guid? addonCategoryId,
        string? categoryName) : base(id, tenantId)
    {
        Name = name;
        MaxPortionsPerCup = maxPortionsPerCup;
        SupplyItemId = supplyItemId;
        ConsumptionQuantityPerPortion = consumptionQuantityPerPortion;
        ConsumptionUnitCode = consumptionUnitCode;
        DisplayOrder = displayOrder;
        Description = description;
        CostPrice = costPrice;
        AddonCategoryId = addonCategoryId;
        CategoryName = categoryName;
        IsActive = true;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Money CostPrice { get; private set; }
    public Guid? AddonCategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public int MaxPortionsPerCup { get; private set; }
    public Guid? SupplyItemId { get; private set; }
    public decimal? ConsumptionQuantityPerPortion { get; private set; }
    public string? ConsumptionUnitCode { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<AcaiFreeTopping> Create(
        Guid tenantId,
        string name,
        int maxPortionsPerCup = 1,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerPortion = null,
        string? consumptionUnitCode = null,
        string? description = null,
        Money? costPrice = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AcaiFreeTopping>(Error.Validation("Acai.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<AcaiFreeTopping>(Error.Validation("Acai.ToppingNameRequired", "Nome do acompanhamento gratuito é obrigatório."));
        if (maxPortionsPerCup <= 0)
            return Result.Failure<AcaiFreeTopping>(Error.Validation("Acai.InvalidMaxPortions", "Quantidade máxima por copo deve ser positiva."));
        if (consumptionQuantityPerPortion.HasValue && consumptionQuantityPerPortion.Value <= 0m)
            return Result.Failure<AcaiFreeTopping>(Error.Validation("Acai.InvalidConsumption", "Quantidade de consumo por porção deve ser positiva."));

        return Result.Success(new AcaiFreeTopping(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            maxPortionsPerCup,
            supplyItemId == Guid.Empty ? null : supplyItemId,
            consumptionQuantityPerPortion,
            string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant(),
            displayOrder,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            costPrice ?? Money.Zero(),
            addonCategoryId == Guid.Empty ? null : addonCategoryId,
            string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim()));
    }

    public Result Update(
        string name,
        int maxPortionsPerCup,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerPortion = null,
        string? consumptionUnitCode = null,
        string? description = null,
        Money? costPrice = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Acai.ToppingNameRequired", "Nome do acompanhamento gratuito é obrigatório."));
        if (maxPortionsPerCup <= 0)
            return Result.Failure(Error.Validation("Acai.InvalidMaxPortions", "Quantidade máxima por copo deve ser positiva."));
        if (consumptionQuantityPerPortion.HasValue && consumptionQuantityPerPortion.Value <= 0m)
            return Result.Failure(Error.Validation("Acai.InvalidConsumption", "Quantidade de consumo por porção deve ser positiva."));

        Name = name.Trim();
        MaxPortionsPerCup = maxPortionsPerCup;
        DisplayOrder = displayOrder;
        SupplyItemId = supplyItemId == Guid.Empty ? null : supplyItemId;
        ConsumptionQuantityPerPortion = consumptionQuantityPerPortion;
        ConsumptionUnitCode = string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant();
        if (description is not null) Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (costPrice is not null) CostPrice = costPrice;
        if (addonCategoryId.HasValue) AddonCategoryId = addonCategoryId == Guid.Empty ? null : addonCategoryId;
        if (categoryName is not null) CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim();
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Adicional pago em área separada dos acompanhamentos gratuitos (Seção 13).
/// </summary>
public sealed class AcaiPaidAddon : TenantScopedAggregateRoot
{
    private AcaiPaidAddon()
    {
        Name = string.Empty;
        AdditionalPrice = Money.Zero();
        CostPrice = Money.Zero();
    }

    private AcaiPaidAddon(
        Guid id,
        Guid tenantId,
        string name,
        Money additionalPrice,
        int maxQuantityPerCup,
        Guid? supplyItemId,
        decimal? consumptionQuantityPerUnit,
        string? consumptionUnitCode,
        int displayOrder,
        string? description,
        Money costPrice,
        Guid? addonCategoryId,
        string? categoryName) : base(id, tenantId)
    {
        Name = name;
        AdditionalPrice = additionalPrice;
        CostPrice = costPrice;
        MaxQuantityPerCup = maxQuantityPerCup;
        SupplyItemId = supplyItemId;
        ConsumptionQuantityPerUnit = consumptionQuantityPerUnit;
        ConsumptionUnitCode = consumptionUnitCode;
        DisplayOrder = displayOrder;
        Description = description;
        AddonCategoryId = addonCategoryId;
        CategoryName = categoryName;
        IsActive = true;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Money AdditionalPrice { get; private set; }
    public Money CostPrice { get; private set; }
    public Guid? AddonCategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public int MaxQuantityPerCup { get; private set; }
    public Guid? SupplyItemId { get; private set; }
    public decimal? ConsumptionQuantityPerUnit { get; private set; }
    public string? ConsumptionUnitCode { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<AcaiPaidAddon> Create(
        Guid tenantId,
        string name,
        Money additionalPrice,
        int maxQuantityPerCup = 3,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerUnit = null,
        string? consumptionUnitCode = null,
        string? description = null,
        Money? costPrice = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AcaiPaidAddon>(Error.Validation("Acai.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<AcaiPaidAddon>(Error.Validation("Acai.AddonNameRequired", "Nome do adicional pago é obrigatório."));
        if (maxQuantityPerCup <= 0)
            return Result.Failure<AcaiPaidAddon>(Error.Validation("Acai.InvalidMaxQty", "Quantidade máxima por copo deve ser positiva."));
        if (consumptionQuantityPerUnit.HasValue && consumptionQuantityPerUnit.Value <= 0m)
            return Result.Failure<AcaiPaidAddon>(Error.Validation("Acai.InvalidConsumption", "Quantidade de consumo por unidade deve ser positiva."));

        ArgumentNullException.ThrowIfNull(additionalPrice);

        return Result.Success(new AcaiPaidAddon(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            additionalPrice,
            maxQuantityPerCup,
            supplyItemId == Guid.Empty ? null : supplyItemId,
            consumptionQuantityPerUnit,
            string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant(),
            displayOrder,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            costPrice ?? Money.Zero(additionalPrice.Currency),
            addonCategoryId == Guid.Empty ? null : addonCategoryId,
            string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim()));
    }

    public Result UpdatePrice(Money newPrice)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        AdditionalPrice = newPrice;
        return Result.Success();
    }

    public Result Update(
        string name,
        Money additionalPrice,
        int maxQuantityPerCup,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerUnit = null,
        string? consumptionUnitCode = null,
        string? description = null,
        Money? costPrice = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Acai.AddonNameRequired", "Nome do adicional pago é obrigatório."));
        if (maxQuantityPerCup <= 0)
            return Result.Failure(Error.Validation("Acai.InvalidMaxQty", "Quantidade máxima por copo deve ser positiva."));
        if (consumptionQuantityPerUnit.HasValue && consumptionQuantityPerUnit.Value <= 0m)
            return Result.Failure(Error.Validation("Acai.InvalidConsumption", "Quantidade de consumo por unidade deve ser positiva."));
        ArgumentNullException.ThrowIfNull(additionalPrice);

        Name = name.Trim();
        AdditionalPrice = additionalPrice;
        MaxQuantityPerCup = maxQuantityPerCup;
        DisplayOrder = displayOrder;
        SupplyItemId = supplyItemId == Guid.Empty ? null : supplyItemId;
        ConsumptionQuantityPerUnit = consumptionQuantityPerUnit;
        ConsumptionUnitCode = string.IsNullOrWhiteSpace(consumptionUnitCode) ? null : consumptionUnitCode.Trim().ToLowerInvariant();
        if (description is not null) Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (costPrice is not null) CostPrice = costPrice;
        if (addonCategoryId.HasValue) AddonCategoryId = addonCategoryId == Guid.Empty ? null : addonCategoryId;
        if (categoryName is not null) CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim();
        return Result.Success();
    }

    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
