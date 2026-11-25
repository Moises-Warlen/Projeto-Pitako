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
/// Tamanho configurável de copo/recipiente de açaí com capacidade em ml, preço base
/// e limite próprio de acompanhamentos gratuitos (Seção 13).
/// </summary>
public sealed class AcaiCupSize : TenantScopedAggregateRoot
{
    private AcaiCupSize()
    {
        Name = string.Empty;
        BasePrice = Money.Zero();
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
        int displayOrder) : base(id, tenantId)
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
        IsActive = true;
    }

    public string Name { get; private set; }
    public int VolumeMl { get; private set; }
    public Money BasePrice { get; private set; }
    public int FreeToppingsLimit { get; private set; }
    public int MinFreeToppings { get; private set; }
    public AcaiFreeCountingMode CountingMode { get; private set; }
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
        int displayOrder = 0)
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
            displayOrder));
    }

    public Result UpdateConfiguration(
        string name,
        int volumeMl,
        Money basePrice,
        int freeToppingsLimit,
        int minFreeToppings,
        AcaiFreeCountingMode countingMode,
        int? maxPaidAddonsPerCup,
        int displayOrder)
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
        return Result.Success();
    }

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
    }

    private AcaiFreeTopping(
        Guid id,
        Guid tenantId,
        string name,
        int maxPortionsPerCup,
        Guid? supplyItemId,
        decimal? consumptionQuantityPerPortion,
        string? consumptionUnitCode,
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        MaxPortionsPerCup = maxPortionsPerCup;
        SupplyItemId = supplyItemId;
        ConsumptionQuantityPerPortion = consumptionQuantityPerPortion;
        ConsumptionUnitCode = consumptionUnitCode;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public string Name { get; private set; }
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
        string? consumptionUnitCode = null)
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
            displayOrder));
    }

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
        int displayOrder) : base(id, tenantId)
    {
        Name = name;
        AdditionalPrice = additionalPrice;
        MaxQuantityPerCup = maxQuantityPerCup;
        SupplyItemId = supplyItemId;
        ConsumptionQuantityPerUnit = consumptionQuantityPerUnit;
        ConsumptionUnitCode = consumptionUnitCode;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public string Name { get; private set; }
    public Money AdditionalPrice { get; private set; }
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
        string? consumptionUnitCode = null)
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
            displayOrder));
    }

    public Result UpdatePrice(Money newPrice)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        AdditionalPrice = newPrice;
        return Result.Success();
    }

    public void Pause() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
