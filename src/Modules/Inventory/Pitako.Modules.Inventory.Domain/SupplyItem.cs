using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Domain;

/// <summary>
/// Registro histórico imutável das alterações de preço de compra e fornecedor de um insumo (Seção 15).
/// </summary>
public sealed class SupplyPriceHistory : Entity<Guid>
{
    private SupplyPriceHistory()
    {
        PreviousPrice = Money.Zero();
        NewPrice = Money.Zero();
        Reason = string.Empty;
    }

    internal SupplyPriceHistory(
        Guid id,
        Guid tenantId,
        Guid supplyItemId,
        Money previousPrice,
        Money newPrice,
        string? supplierName,
        Guid? changedByUserId,
        string reason,
        DateTimeOffset changedAtUtc) : base(id)
    {
        TenantId = tenantId;
        SupplyItemId = supplyItemId;
        // Instâncias independentes evitam compartilhamento de referência em entidades owned (OwnsOne) no EF Core.
        PreviousPrice = Money.Create(previousPrice.Amount, previousPrice.Currency, allowNegative: true).Value;
        NewPrice = Money.Create(newPrice.Amount, newPrice.Currency, allowNegative: true).Value;
        SupplierName = supplierName;
        ChangedByUserId = changedByUserId;
        Reason = reason;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Money PreviousPrice { get; private set; }
    public Money NewPrice { get; private set; }
    public string? SupplierName { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }
}

/// <summary>
/// Insumo cadastrado pelo estabelecimento com unidade de compra, conversão opcional de embalagem,
/// fornecedor e histórico completo de custos (Seção 15).
/// </summary>
public sealed class SupplyItem : TenantScopedAggregateRoot
{
    private readonly List<SupplyPriceHistory> _priceHistory = new();

    private SupplyItem()
    {
        Name = string.Empty;
        PurchaseUnitCode = "kg";
        ContentUnitCode = "kg";
        PurchasePrice = Money.Zero();
    }

    private SupplyItem(
        Guid id,
        Guid tenantId,
        string name,
        string? sku,
        string purchaseUnitCode,
        decimal purchaseQuantity,
        string contentUnitCode,
        decimal contentQuantityPerPackage,
        Money purchasePrice,
        string? supplierName) : base(id, tenantId)
    {
        Name = name;
        Sku = sku;
        PurchaseUnitCode = purchaseUnitCode;
        PurchaseQuantity = purchaseQuantity;
        ContentUnitCode = contentUnitCode;
        ContentQuantityPerPackage = contentQuantityPerPackage;
        PurchasePrice = purchasePrice;
        SupplierName = supplierName;
        IsActive = true;
    }

    public string Name { get; private set; }
    public string? Sku { get; private set; }
    public string PurchaseUnitCode { get; private set; }
    public decimal PurchaseQuantity { get; private set; }
    public string ContentUnitCode { get; private set; }
    public decimal ContentQuantityPerPackage { get; private set; }
    public Money PurchasePrice { get; private set; }
    public string? SupplierName { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<SupplyPriceHistory> PriceHistory => _priceHistory.AsReadOnly();

    public static Result<SupplyItem> Create(
        Guid tenantId,
        string name,
        Money purchasePrice,
        decimal purchaseQuantity,
        string purchaseUnitCode,
        string? supplierName = null,
        string? sku = null,
        decimal? contentQuantityPerPackage = null,
        string? contentUnitCode = null,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits = null,
        DateTimeOffset? createdAtUtc = null,
        Guid? createdByUserId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SupplyItem>(Error.Validation("Supply.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<SupplyItem>(Error.Validation("Supply.NameRequired", "Nome do insumo é obrigatório."));

        if (purchaseQuantity <= 0m)
            return Result.Failure<SupplyItem>(Error.Validation("Supply.InvalidQuantity", "Quantidade de compra do insumo deve ser positiva."));

        ArgumentNullException.ThrowIfNull(purchasePrice);

        var purchaseUnitRes = UnitConverter.Resolve(purchaseUnitCode, customUnits);
        if (purchaseUnitRes.IsFailure)
            return Result.Failure<SupplyItem>(purchaseUnitRes.Error);

        var effectiveContentUnit = string.IsNullOrWhiteSpace(contentUnitCode)
            ? purchaseUnitRes.Value.Code
            : UnitConverter.NormalizeCode(contentUnitCode);

        var contentUnitRes = UnitConverter.Resolve(effectiveContentUnit, customUnits);
        if (contentUnitRes.IsFailure)
            return Result.Failure<SupplyItem>(contentUnitRes.Error);

        var effectiveContentQty = contentQuantityPerPackage ?? purchaseQuantity;
        if (effectiveContentQty <= 0m)
            return Result.Failure<SupplyItem>(Error.Validation("Supply.InvalidContentQty", "Quantidade contida deve ser positiva."));

        var item = new SupplyItem(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant(),
            purchaseUnitRes.Value.Code,
            purchaseQuantity,
            contentUnitRes.Value.Code,
            effectiveContentQty,
            purchasePrice,
            string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim());

        item._priceHistory.Add(new SupplyPriceHistory(
            Guid.NewGuid(),
            tenantId,
            item.Id,
            Money.Zero(purchasePrice.Currency),
            purchasePrice,
            item.SupplierName,
            createdByUserId,
            "Cadastro inicial do insumo",
            createdAtUtc ?? DateTimeOffset.UtcNow));

        return Result.Success(item);
    }

    public Result UpdatePurchasePrice(
        Money newPrice,
        string? supplierName,
        string reason,
        DateTimeOffset utcNow,
        Guid? actorUserId)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Supply.ReasonRequired", "Motivo da alteração de preço é obrigatório."));

        var previous = PurchasePrice;
        PurchasePrice = newPrice;
        if (supplierName is not null)
            SupplierName = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();

        _priceHistory.Add(new SupplyPriceHistory(
            Guid.NewGuid(),
            TenantId,
            Id,
            previous,
            newPrice,
            SupplierName,
            actorUserId,
            reason.Trim(),
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Calcula o custo exato (4 casas decimais) de uma quantidade utilizada na unidade informada,
    /// aplicando conversão compatível com a unidade de conteúdo/compra do insumo.
    /// </summary>
    public Result<decimal> CalculateCostForUsage(
        decimal usageQuantity,
        string usageUnitCode,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits = null)
    {
        if (usageQuantity < 0m)
            return Result.Failure<decimal>(Error.Validation("Supply.NegativeUsage", "Quantidade de uso não pode ser negativa."));

        var normalizedUsageUnit = UnitConverter.NormalizeCode(usageUnitCode);

        // Se a ficha usa a própria unidade de compra (ex.: 1 caixa), calcula diretamente sobre PurchaseQuantity.
        decimal factorUsed;
        if (normalizedUsageUnit == PurchaseUnitCode && ContentUnitCode != PurchaseUnitCode)
        {
            factorUsed = usageQuantity / PurchaseQuantity;
        }
        else
        {
            var convertedRes = UnitConverter.Convert(usageQuantity, normalizedUsageUnit, ContentUnitCode, customUnits);
            if (convertedRes.IsFailure)
                return Result.Failure<decimal>(convertedRes.Error);

            factorUsed = convertedRes.Value / ContentQuantityPerPackage;
        }

        var cost = decimal.Round(PurchasePrice.Amount * factorUsed, 4, MidpointRounding.ToEven);
        return Result.Success(cost);
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
