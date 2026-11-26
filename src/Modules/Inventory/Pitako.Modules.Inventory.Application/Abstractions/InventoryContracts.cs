using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Inventory.Application.Abstractions;

public interface ITenantCustomUnitRepository
{
    Task<IReadOnlyDictionary<string, UnitDefinition>> GetLookupAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TenantCustomUnit unit, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISupplyItemRepository
{
    Task<SupplyItem?> FindByIdAsync(Guid supplyItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, SupplyItem>> FindLookupByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplyItem>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SupplyItem item, CancellationToken cancellationToken = default);
    void Remove(SupplyItem item);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITechnicalSheetRepository
{
    Task<TechnicalSheet?> FindByIdAsync(Guid sheetId, CancellationToken cancellationToken = default);
    Task<TechnicalSheet?> FindActiveByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TechnicalSheet>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TechnicalSheet>> ListUsingSupplyItemAsync(Guid supplyItemId, CancellationToken cancellationToken = default);
    Task AddAsync(TechnicalSheet sheet, CancellationToken cancellationToken = default);
    void Remove(TechnicalSheet sheet);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStockBalanceRepository
{
    Task<StockBalance?> FindBySupplyItemIdAsync(Guid supplyItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, StockBalance>> FindLookupBySupplyItemIdsAsync(IEnumerable<Guid> supplyItemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockBalance>> ListAllWithLotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMovement>> ListByReferenceAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken = default);
    Task AddAsync(StockBalance balance, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPurchaseReceiptRepository
{
    Task<PurchaseReceipt?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);
    Task<PurchaseReceipt?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task AddAsync(PurchaseReceipt receipt, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IProductionOrderRepository
{
    Task<ProductionOrder?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record OrderCustomIngredientInput(Guid SupplyItemId, decimal Quantity, string UnitCode, string IngredientName);

public sealed record OrderStockLineInput(
    Guid ProductId,
    decimal Quantity,
    string UnitCode = "unidade",
    IReadOnlyList<OrderCustomIngredientInput>? CustomIngredients = null);

public sealed record StockOrderDeductionSummary(int MovementsCreated, int SkippedItemsCount);

/// <summary>
/// Contrato público para consumo futuro pelo módulo de Pedidos (Fase 13) na baixa automática
/// de estoque via ficha técnica e estorno automático em cancelamentos.
/// </summary>
public interface IInventoryOrderDeductionService
{
    Task<Result<StockOrderDeductionSummary>> DeductForOrderSaleAsync(
        User actor,
        Guid orderId,
        string orderReferenceLabel,
        IEnumerable<OrderStockLineInput> orderLines,
        CancellationToken cancellationToken = default);

    Task<Result<int>> ReverseOrderDeductionAsync(
        User actor,
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default);
}
