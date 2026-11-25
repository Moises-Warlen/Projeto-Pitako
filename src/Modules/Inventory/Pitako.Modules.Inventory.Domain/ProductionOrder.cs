using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Inventory.Domain;

public enum ProductionOrderStatus
{
    Completed = 1,
    Cancelled = 2
}

/// <summary>
/// Consumo proporcional de insumo registrado em uma ordem de produção interna (Seção 16).
/// </summary>
public sealed class ProductionIngredientConsumption : Entity<Guid>, ITenantScopedEntity
{
    private ProductionIngredientConsumption()
    {
        SupplyNameSnapshot = string.Empty;
        StockUnitCode = "unidade";
    }

    internal ProductionIngredientConsumption(
        Guid id,
        Guid tenantId,
        Guid productionOrderId,
        Guid supplyItemId,
        string supplyNameSnapshot,
        decimal quantityConsumedInStockUnit,
        string stockUnitCode,
        decimal calculatedCost) : base(id)
    {
        TenantId = tenantId;
        ProductionOrderId = productionOrderId;
        SupplyItemId = supplyItemId;
        SupplyNameSnapshot = supplyNameSnapshot;
        QuantityConsumedInStockUnit = decimal.Round(quantityConsumedInStockUnit, 4, MidpointRounding.ToEven);
        StockUnitCode = stockUnitCode;
        CalculatedCost = decimal.Round(calculatedCost, 4, MidpointRounding.ToEven);
    }

    public Guid TenantId { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public string SupplyNameSnapshot { get; private set; }
    public decimal QuantityConsumedInStockUnit { get; private set; }
    public string StockUnitCode { get; private set; }
    public decimal CalculatedCost { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));

        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em ProductionIngredientConsumption.");

        TenantId = tenantId;
    }
}

/// <summary>
/// Ordem de produção baseada em ficha técnica: consome matérias-primas em lotes FEFO,
/// calcula custo total/unitário e credita opcionalmente o lote produzido no estoque (Seção 16).
/// </summary>
public sealed class ProductionOrder : TenantScopedAggregateRoot
{
    private readonly List<ProductionIngredientConsumption> _consumptions = new();

    private ProductionOrder()
    {
        TechnicalSheetNameSnapshot = string.Empty;
        YieldUnitCode = "unidade";
    }

    private ProductionOrder(
        Guid id,
        Guid tenantId,
        Guid technicalSheetId,
        string technicalSheetNameSnapshot,
        int technicalSheetVersion,
        Guid? outputSupplyItemId,
        decimal batchMultiplier,
        decimal producedQuantity,
        string yieldUnitCode,
        string? batchNumber,
        DateTimeOffset? expiresAtUtc,
        decimal totalProductionCost,
        decimal unitProductionCost,
        Guid? executedByUserId,
        DateTimeOffset executedAtUtc,
        string? notes) : base(id, tenantId)
    {
        TechnicalSheetId = technicalSheetId;
        TechnicalSheetNameSnapshot = technicalSheetNameSnapshot;
        TechnicalSheetVersion = technicalSheetVersion;
        OutputSupplyItemId = outputSupplyItemId;
        BatchMultiplier = batchMultiplier;
        ProducedQuantity = decimal.Round(producedQuantity, 4, MidpointRounding.ToEven);
        YieldUnitCode = yieldUnitCode;
        BatchNumber = batchNumber;
        ExpiresAtUtc = expiresAtUtc;
        TotalProductionCost = decimal.Round(totalProductionCost, 4, MidpointRounding.ToEven);
        UnitProductionCost = decimal.Round(unitProductionCost, 4, MidpointRounding.ToEven);
        Status = ProductionOrderStatus.Completed;
        ExecutedByUserId = executedByUserId;
        ExecutedAtUtc = executedAtUtc;
        Notes = notes;
    }

    public Guid TechnicalSheetId { get; private set; }
    public string TechnicalSheetNameSnapshot { get; private set; }
    public int TechnicalSheetVersion { get; private set; }
    public Guid? OutputSupplyItemId { get; private set; }
    public decimal BatchMultiplier { get; private set; }
    public decimal ProducedQuantity { get; private set; }
    public string YieldUnitCode { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public decimal TotalProductionCost { get; private set; }
    public decimal UnitProductionCost { get; private set; }
    public ProductionOrderStatus Status { get; private set; }
    public Guid? ExecutedByUserId { get; private set; }
    public DateTimeOffset ExecutedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<ProductionIngredientConsumption> Consumptions => _consumptions.AsReadOnly();

    public static Result<ProductionOrder> CreateCompleted(
        Guid tenantId,
        TechnicalSheet sheet,
        decimal batchMultiplier,
        Guid? outputSupplyItemId,
        string? batchNumber,
        DateTimeOffset? expiresAtUtc,
        Guid? executedByUserId,
        DateTimeOffset executedAtUtc,
        string? notes,
        IEnumerable<(Guid SupplyItemId, string SupplyName, decimal ConsumedQty, string StockUnitCode, decimal TotalCost)> consumptions)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (tenantId == Guid.Empty)
            return Result.Failure<ProductionOrder>(Error.Validation("Production.TenantRequired", "TenantId é obrigatório."));

        if (batchMultiplier <= 0m)
            return Result.Failure<ProductionOrder>(Error.Validation("Production.InvalidMultiplier", "Multiplicador de produção deve ser positivo."));

        var consumptionList = consumptions.ToList();
        var totalCost = decimal.Round(consumptionList.Sum(c => c.TotalCost), 4, MidpointRounding.ToEven);
        var producedQty = decimal.Round(sheet.YieldQuantity * batchMultiplier, 4, MidpointRounding.ToEven);
        var unitCost = producedQty > 0m
            ? decimal.Round(totalCost / producedQty, 4, MidpointRounding.ToEven)
            : 0m;

        var orderId = Guid.NewGuid();
        var order = new ProductionOrder(
            orderId,
            tenantId,
            sheet.Id,
            sheet.Name,
            sheet.Version,
            outputSupplyItemId == Guid.Empty ? null : outputSupplyItemId,
            batchMultiplier,
            producedQty,
            sheet.YieldUnitCode,
            string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim().ToUpperInvariant(),
            expiresAtUtc,
            totalCost,
            unitCost,
            executedByUserId,
            executedAtUtc,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

        foreach (var item in consumptionList)
        {
            order._consumptions.Add(new ProductionIngredientConsumption(
                Guid.NewGuid(),
                tenantId,
                orderId,
                item.SupplyItemId,
                item.SupplyName,
                item.ConsumedQty,
                item.StockUnitCode,
                item.TotalCost));
        }

        return Result.Success(order);
    }
}
