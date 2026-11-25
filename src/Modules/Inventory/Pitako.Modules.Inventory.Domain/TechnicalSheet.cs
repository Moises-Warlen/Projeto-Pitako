using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Inventory.Domain;

public sealed class TechnicalSheetIngredient : Entity<Guid>
{
    private TechnicalSheetIngredient()
    {
        SupplyItemNameSnapshot = string.Empty;
        UnitCode = string.Empty;
    }

    internal TechnicalSheetIngredient(
        Guid id,
        Guid tenantId,
        Guid technicalSheetId,
        Guid supplyItemId,
        string supplyItemNameSnapshot,
        decimal quantity,
        string unitCode,
        decimal calculatedCost) : base(id)
    {
        TenantId = tenantId;
        TechnicalSheetId = technicalSheetId;
        SupplyItemId = supplyItemId;
        SupplyItemNameSnapshot = supplyItemNameSnapshot;
        Quantity = quantity;
        UnitCode = unitCode;
        CalculatedCost = calculatedCost;
    }

    public Guid TenantId { get; private set; }
    public Guid TechnicalSheetId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public string SupplyItemNameSnapshot { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitCode { get; private set; }
    public decimal CalculatedCost { get; private set; }

    internal void UpdateCalculatedCost(string supplyName, decimal cost)
    {
        SupplyItemNameSnapshot = supplyName;
        CalculatedCost = cost;
    }
}

public sealed class TechnicalSheetPackaging : Entity<Guid>
{
    private TechnicalSheetPackaging()
    {
        Description = string.Empty;
        UnitCode = "unidade";
    }

    internal TechnicalSheetPackaging(
        Guid id,
        Guid tenantId,
        Guid technicalSheetId,
        Guid? supplyItemId,
        string description,
        decimal quantity,
        string unitCode,
        decimal calculatedCost) : base(id)
    {
        TenantId = tenantId;
        TechnicalSheetId = technicalSheetId;
        SupplyItemId = supplyItemId;
        Description = description;
        Quantity = quantity;
        UnitCode = unitCode;
        CalculatedCost = calculatedCost;
    }

    public Guid TenantId { get; private set; }
    public Guid TechnicalSheetId { get; private set; }
    public Guid? SupplyItemId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitCode { get; private set; }
    public decimal CalculatedCost { get; private set; }

    internal void UpdateCalculatedCost(decimal cost) => CalculatedCost = cost;
}

public sealed class TechnicalSheetCostSnapshot : Entity<Guid>
{
    private TechnicalSheetCostSnapshot()
    {
        TriggerReason = string.Empty;
    }

    internal TechnicalSheetCostSnapshot(
        Guid id,
        Guid tenantId,
        Guid technicalSheetId,
        int version,
        decimal ingredientsCost,
        decimal packagingsCost,
        decimal totalCost,
        decimal yieldQuantity,
        decimal costPerPortion,
        string triggerReason,
        DateTimeOffset recordedAtUtc) : base(id)
    {
        TenantId = tenantId;
        TechnicalSheetId = technicalSheetId;
        Version = version;
        IngredientsCost = ingredientsCost;
        PackagingsCost = packagingsCost;
        TotalCost = totalCost;
        YieldQuantity = yieldQuantity;
        CostPerPortion = costPerPortion;
        TriggerReason = triggerReason;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid TechnicalSheetId { get; private set; }
    public int Version { get; private set; }
    public decimal IngredientsCost { get; private set; }
    public decimal PackagingsCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal YieldQuantity { get; private set; }
    public decimal CostPerPortion { get; private set; }
    public string TriggerReason { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
}

public sealed record IngredientInput(Guid SupplyItemId, decimal Quantity, string UnitCode);
public sealed record PackagingInput(Guid? SupplyItemId, string Description, decimal Quantity, string UnitCode, decimal? ManualTotalCost = null);

/// <summary>
/// Ficha técnica de produção com ingredientes, embalagens, rendimento, custo total,
/// custo por porção e histórico versionado de custos (Seção 15).
/// </summary>
public sealed class TechnicalSheet : TenantScopedAggregateRoot
{
    private readonly List<TechnicalSheetIngredient> _ingredients = new();
    private readonly List<TechnicalSheetPackaging> _packagings = new();
    private readonly List<TechnicalSheetCostSnapshot> _costHistory = new();

    private TechnicalSheet()
    {
        Name = string.Empty;
        YieldUnitCode = "unidade";
    }

    private TechnicalSheet(
        Guid id,
        Guid tenantId,
        Guid? productId,
        string name,
        decimal yieldQuantity,
        string yieldUnitCode,
        string? notes) : base(id, tenantId)
    {
        ProductId = productId;
        Name = name;
        YieldQuantity = yieldQuantity;
        YieldUnitCode = yieldUnitCode;
        Notes = notes;
        Version = 0;
        IsActive = true;
    }

    public Guid? ProductId { get; private set; }
    public string Name { get; private set; }
    public decimal YieldQuantity { get; private set; }
    public string YieldUnitCode { get; private set; }
    public string? Notes { get; private set; }
    public decimal IngredientsTotalCost { get; private set; }
    public decimal PackagingsTotalCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal CostPerPortion { get; private set; }
    public int Version { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<TechnicalSheetIngredient> Ingredients => _ingredients.AsReadOnly();
    public IReadOnlyCollection<TechnicalSheetPackaging> Packagings => _packagings.AsReadOnly();
    public IReadOnlyCollection<TechnicalSheetCostSnapshot> CostHistory => _costHistory.AsReadOnly();

    public static Result<TechnicalSheet> Create(
        Guid tenantId,
        string name,
        decimal yieldQuantity,
        string yieldUnitCode,
        IEnumerable<IngredientInput> ingredients,
        IEnumerable<PackagingInput> packagings,
        IReadOnlyDictionary<Guid, SupplyItem> supplyLookup,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits,
        DateTimeOffset utcNow,
        Guid? productId = null,
        string? notes = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TechnicalSheet>(Error.Validation("TechnicalSheet.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<TechnicalSheet>(Error.Validation("TechnicalSheet.NameRequired", "Nome da ficha técnica é obrigatório."));

        if (yieldQuantity <= 0m)
            return Result.Failure<TechnicalSheet>(Error.Validation("TechnicalSheet.InvalidYield", "Rendimento deve ser positivo."));

        var sheet = new TechnicalSheet(
            Guid.NewGuid(),
            tenantId,
            productId == Guid.Empty ? null : productId,
            name.Trim(),
            yieldQuantity,
            UnitConverter.NormalizeCode(yieldUnitCode),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

        var compositionRes = sheet.ReplaceCompositionAndRecalculate(
            ingredients,
            packagings,
            supplyLookup,
            customUnits,
            "Criação inicial da ficha técnica",
            utcNow);

        if (compositionRes.IsFailure)
            return Result.Failure<TechnicalSheet>(compositionRes.Error);

        return Result.Success(sheet);
    }

    public Result ReplaceCompositionAndRecalculate(
        IEnumerable<IngredientInput> ingredients,
        IEnumerable<PackagingInput> packagings,
        IReadOnlyDictionary<Guid, SupplyItem> supplyLookup,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits,
        string reason,
        DateTimeOffset utcNow)
    {
        var ingList = ingredients.ToList();
        if (ingList.Count == 0)
            return Result.Failure(Error.Validation("TechnicalSheet.NoIngredients", "Ficha técnica exige ao menos 1 ingrediente."));

        var newIngredients = new List<TechnicalSheetIngredient>();
        foreach (var ing in ingList)
        {
            if (ing.Quantity <= 0m)
                return Result.Failure(Error.Validation("TechnicalSheet.InvalidIngredientQty", "Quantidade do ingrediente deve ser positiva."));

            if (!supplyLookup.TryGetValue(ing.SupplyItemId, out var supply) || supply.TenantId != TenantId)
                return Result.Failure(Error.NotFound("TechnicalSheet.SupplyNotFound", "Insumo não encontrado neste estabelecimento."));

            var costRes = supply.CalculateCostForUsage(ing.Quantity, ing.UnitCode, customUnits);
            if (costRes.IsFailure)
                return Result.Failure(costRes.Error);

            newIngredients.Add(new TechnicalSheetIngredient(
                Guid.NewGuid(),
                TenantId,
                Id,
                supply.Id,
                supply.Name,
                ing.Quantity,
                UnitConverter.NormalizeCode(ing.UnitCode),
                costRes.Value));
        }

        var newPackagings = new List<TechnicalSheetPackaging>();
        foreach (var pkg in packagings)
        {
            if (pkg.Quantity <= 0m)
                return Result.Failure(Error.Validation("TechnicalSheet.InvalidPkgQty", "Quantidade da embalagem deve ser positiva."));

            decimal pkgCost;
            if (pkg.SupplyItemId.HasValue)
            {
                if (!supplyLookup.TryGetValue(pkg.SupplyItemId.Value, out var supply) || supply.TenantId != TenantId)
                    return Result.Failure(Error.NotFound("TechnicalSheet.PackagingSupplyNotFound", "Insumo de embalagem não encontrado neste estabelecimento."));

                var costRes = supply.CalculateCostForUsage(pkg.Quantity, pkg.UnitCode, customUnits);
                if (costRes.IsFailure)
                    return Result.Failure(costRes.Error);

                pkgCost = costRes.Value;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(pkg.Description) || pkg.ManualTotalCost is null || pkg.ManualTotalCost < 0m)
                    return Result.Failure(Error.Validation("TechnicalSheet.InvalidPackaging", "Embalagem avulsa exige descrição e custo válido."));

                pkgCost = decimal.Round(pkg.ManualTotalCost.Value, 4, MidpointRounding.ToEven);
            }

            newPackagings.Add(new TechnicalSheetPackaging(
                Guid.NewGuid(),
                TenantId,
                Id,
                pkg.SupplyItemId,
                string.IsNullOrWhiteSpace(pkg.Description) ? "Embalagem" : pkg.Description.Trim(),
                pkg.Quantity,
                UnitConverter.NormalizeCode(pkg.UnitCode),
                pkgCost));
        }

        _ingredients.Clear();
        _ingredients.AddRange(newIngredients);
        _packagings.Clear();
        _packagings.AddRange(newPackagings);

        ComputeTotalsAndRecordSnapshot(reason, utcNow);
        return Result.Success();
    }

    public Result RecalculateFromCurrentSupplies(
        IReadOnlyDictionary<Guid, SupplyItem> supplyLookup,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits,
        string reason,
        DateTimeOffset utcNow)
    {
        foreach (var ing in _ingredients)
        {
            if (!supplyLookup.TryGetValue(ing.SupplyItemId, out var supply))
                return Result.Failure(Error.NotFound("TechnicalSheet.SupplyNotFound", $"Insumo '{ing.SupplyItemNameSnapshot}' não encontrado."));

            var costRes = supply.CalculateCostForUsage(ing.Quantity, ing.UnitCode, customUnits);
            if (costRes.IsFailure)
                return Result.Failure(costRes.Error);

            ing.UpdateCalculatedCost(supply.Name, costRes.Value);
        }

        foreach (var pkg in _packagings.Where(p => p.SupplyItemId.HasValue))
        {
            if (!supplyLookup.TryGetValue(pkg.SupplyItemId!.Value, out var supply))
                return Result.Failure(Error.NotFound("TechnicalSheet.PackagingSupplyNotFound", "Insumo de embalagem não encontrado."));

            var costRes = supply.CalculateCostForUsage(pkg.Quantity, pkg.UnitCode, customUnits);
            if (costRes.IsFailure)
                return Result.Failure(costRes.Error);

            pkg.UpdateCalculatedCost(costRes.Value);
        }

        ComputeTotalsAndRecordSnapshot(reason, utcNow);
        return Result.Success();
    }

    private void ComputeTotalsAndRecordSnapshot(string reason, DateTimeOffset utcNow)
    {
        IngredientsTotalCost = decimal.Round(_ingredients.Sum(i => i.CalculatedCost), 4, MidpointRounding.ToEven);
        PackagingsTotalCost = decimal.Round(_packagings.Sum(p => p.CalculatedCost), 4, MidpointRounding.ToEven);
        TotalCost = decimal.Round(IngredientsTotalCost + PackagingsTotalCost, 4, MidpointRounding.ToEven);
        CostPerPortion = decimal.Round(TotalCost / YieldQuantity, 4, MidpointRounding.ToEven);
        Version++;

        _costHistory.Add(new TechnicalSheetCostSnapshot(
            Guid.NewGuid(),
            TenantId,
            Id,
            Version,
            IngredientsTotalCost,
            PackagingsTotalCost,
            TotalCost,
            YieldQuantity,
            CostPerPortion,
            reason,
            utcNow));
    }
}
