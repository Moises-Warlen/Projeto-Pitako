using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Application.Services;

public sealed record LowStockAlertItem(
    Guid SupplyItemId,
    string SupplyName,
    string UnitCode,
    decimal QuantityOnHand,
    decimal MinimumStockQuantity);

public sealed record LotExpiryAlertItem(
    Guid StockLotId,
    Guid SupplyItemId,
    string SupplyName,
    string BatchNumber,
    decimal RemainingQuantity,
    string UnitCode,
    DateTimeOffset ExpiresAtUtc,
    bool IsExpired);

public sealed record InventoryAlertsSnapshot(
    IReadOnlyList<LowStockAlertItem> LowStockAlerts,
    IReadOnlyList<LotExpiryAlertItem> ExpiredLots,
    IReadOnlyList<LotExpiryAlertItem> ExpiringSoonLots);

/// <summary>
/// Serviço de aplicação para gestão operacional de estoque, lotes FEFO, compras de fornecedores,
/// ordens de produção via ficha técnica, alertas e baixa automática em vendas (Seção 16).
/// </summary>
public sealed class InventoryStockAndProductionService : IInventoryOrderDeductionService
{
    private readonly ITenantCustomUnitRepository _unitRepo;
    private readonly ISupplyItemRepository _supplyRepo;
    private readonly ITechnicalSheetRepository _sheetRepo;
    private readonly IStockBalanceRepository _stockRepo;
    private readonly IPurchaseReceiptRepository _purchaseRepo;
    private readonly IProductionOrderRepository _productionRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public InventoryStockAndProductionService(
        ITenantCustomUnitRepository unitRepo,
        ISupplyItemRepository supplyRepo,
        ITechnicalSheetRepository sheetRepo,
        IStockBalanceRepository stockRepo,
        IPurchaseReceiptRepository purchaseRepo,
        IProductionOrderRepository productionRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _unitRepo = unitRepo;
        _supplyRepo = supplyRepo;
        _sheetRepo = sheetRepo;
        _stockRepo = stockRepo;
        _purchaseRepo = purchaseRepo;
        _productionRepo = productionRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<StockBalance>> ConfigureStockBalanceAsync(
        User actor,
        Guid supplyItemId,
        string controlUnitCode,
        decimal minimumStockQuantity = 0m,
        bool allowNegativeStock = false,
        bool autoDeductOnOrder = true,
        bool blockExpiredLots = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<StockBalance>(guard.Error);

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        if (supply is null)
            return Result.Failure<StockBalance>(Error.NotFound("Supply.NotFound", "Insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, supply);

        var existing = await _stockRepo.FindBySupplyItemIdAsync(supplyItemId, cancellationToken);
        if (existing is not null)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existing);
            var updateRes = existing.UpdatePolicy(minimumStockQuantity, allowNegativeStock, autoDeductOnOrder, blockExpiredLots);
            if (updateRes.IsFailure)
                return Result.Failure<StockBalance>(updateRes.Error);

            await _stockRepo.SaveChangesAsync(cancellationToken);
            return Result.Success(existing);
        }

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var created = StockBalance.Create(
            guard.Value,
            supply,
            controlUnitCode,
            minimumStockQuantity,
            allowNegativeStock,
            autoDeductOnOrder,
            blockExpiredLots,
            customUnits);

        if (created.IsFailure)
            return created;

        await _stockRepo.AddAsync(created.Value, cancellationToken);
        await _stockRepo.SaveChangesAsync(cancellationToken);
        return created;
    }

    /// <summary>
    /// Registra entrada de compra convertendo automaticamente unidades de compra para a unidade de estoque,
    /// criando lotes com validade e atualizando custo/fichas técnicas quando solicitado.
    /// </summary>
    public async Task<Result<PurchaseReceipt>> ReceivePurchaseAsync(
        User actor,
        string supplierName,
        string? invoiceNumber,
        IEnumerable<PurchaseLineInput> lines,
        bool updateSupplyPrice = true,
        string? notes = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<PurchaseReceipt>(guard.Error);

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existingReceipt = await _purchaseRepo.FindByIdempotencyKeyAsync(normalizedKey, cancellationToken);
            if (existingReceipt is not null)
                return Result.Success(existingReceipt);
        }

        var lineList = lines.ToList();
        if (lineList.Count == 0)
            return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.EmptyItems", "Entrada de compra exige ao menos 1 item."));

        var supplyIds = lineList.Select(l => l.SupplyItemId).Distinct().ToList();
        var supplies = await _supplyRepo.FindLookupByIdsAsync(supplyIds, cancellationToken);
        var balances = (await _stockRepo.FindLookupBySupplyItemIdsAsync(supplyIds, cancellationToken))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);

        var resolvedLines = new List<(PurchaseLineInput Line, SupplyItem Supply, decimal CreditedQty, string StockUnit)>();
        var utcNow = _clock.UtcNow;

        foreach (var line in lineList)
        {
            if (!supplies.TryGetValue(line.SupplyItemId, out var supply))
                return Result.Failure<PurchaseReceipt>(Error.NotFound("Supply.NotFound", "Insumo informado na compra não foi encontrado."));

            TenantGuard.EnsureOwnership(_tenantContext, supply);

            if (!balances.TryGetValue(supply.Id, out var balance))
            {
                var defaultBalance = StockBalance.Create(
                    guard.Value,
                    supply,
                    supply.ContentUnitCode,
                    customUnits: customUnits);

                if (defaultBalance.IsFailure)
                    return Result.Failure<PurchaseReceipt>(defaultBalance.Error);

                balance = defaultBalance.Value;
                balances[supply.Id] = balance;
                await _stockRepo.AddAsync(balance, cancellationToken);
            }

            var convertedRes = supply.ConvertQuantity(line.PurchasedQuantity, line.PurchaseUnitCode, balance.UnitCode, customUnits);
            if (convertedRes.IsFailure)
                return Result.Failure<PurchaseReceipt>(convertedRes.Error);

            if (convertedRes.Value <= 0m)
                return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.InvalidConvertedQty", "Quantidade convertida para estoque deve ser positiva."));

            resolvedLines.Add((line, supply, convertedRes.Value, balance.UnitCode));
        }

        var receiptRes = PurchaseReceipt.Create(
            guard.Value,
            supplierName,
            invoiceNumber,
            notes,
            actor.Id,
            utcNow,
            resolvedLines,
            normalizedKey);

        if (receiptRes.IsFailure)
            return receiptRes;

        var receipt = receiptRes.Value;
        await _purchaseRepo.AddAsync(receipt, cancellationToken);

        for (var i = 0; i < resolvedLines.Count; i++)
        {
            var (line, supply, creditedQty, _) = resolvedLines[i];
            var balance = balances[supply.Id];
            var lineTotal = decimal.Round(line.UnitPriceAmount * line.PurchasedQuantity, 2, MidpointRounding.ToEven);
            var stockUnitCost = decimal.Round(lineTotal / creditedQty, 4, MidpointRounding.ToEven);

            var entryRes = balance.RegisterEntry(
                creditedQty,
                stockUnitCost,
                StockMovementType.PurchaseEntry,
                $"Entrada compra {(string.IsNullOrWhiteSpace(receipt.InvoiceNumber) ? receipt.SupplierName : $"NF {receipt.InvoiceNumber}")}",
                utcNow,
                actor.Id,
                line.BatchNumber,
                line.ManufacturedAtUtc,
                line.ExpiresAtUtc,
                "PurchaseReceipt",
                receipt.Id,
                normalizedKey is null ? null : $"{normalizedKey}:line:{i}");

            if (entryRes.IsFailure)
                return Result.Failure<PurchaseReceipt>(entryRes.Error);

            if (updateSupplyPrice && UnitConverter.NormalizeCode(line.PurchaseUnitCode) == supply.PurchaseUnitCode)
            {
                var equivalentPurchasePrice = decimal.Round(line.UnitPriceAmount * supply.PurchaseQuantity, 2, MidpointRounding.ToEven);
                var moneyRes = Money.Create(equivalentPurchasePrice, supply.PurchasePrice.Currency);
                if (moneyRes.IsFailure)
                    return Result.Failure<PurchaseReceipt>(moneyRes.Error);

                if (moneyRes.Value.Amount != supply.PurchasePrice.Amount)
                {
                    supply.UpdatePurchasePrice(
                        moneyRes.Value,
                        receipt.SupplierName,
                        $"Atualizado pela compra {(string.IsNullOrWhiteSpace(receipt.InvoiceNumber) ? receipt.Id.ToString("N")[..8] : $"NF {receipt.InvoiceNumber}")}",
                        utcNow,
                        actor.Id);

                    var affectedSheets = await _sheetRepo.ListUsingSupplyItemAsync(supply.Id, cancellationToken);
                    if (affectedSheets.Count > 0)
                    {
                        var sheetSupplyIds = affectedSheets
                            .SelectMany(s => s.Ingredients.Select(ing => ing.SupplyItemId)
                                .Concat(s.Packagings.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value)))
                            .Distinct()
                            .ToList();

                        var lookup = (await _supplyRepo.FindLookupByIdsAsync(sheetSupplyIds, cancellationToken))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                        lookup[supply.Id] = supply;

                        foreach (var sheet in affectedSheets)
                        {
                            var recalc = sheet.RecalculateFromCurrentSupplies(
                                lookup,
                                customUnits,
                                $"Reajuste por entrada de compra ({supply.Name})",
                                utcNow);
                            if (recalc.IsFailure)
                                return Result.Failure<PurchaseReceipt>(recalc.Error);
                        }
                    }
                }
            }
        }

        await _purchaseRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(receipt);
    }

    public async Task<Result<StockMovement>> RegisterManualAdjustmentAsync(
        User actor,
        Guid supplyItemId,
        decimal quantityDeltaInControlUnit,
        string reason,
        string? batchNumber = null,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<StockMovement>(guard.Error);

        if (quantityDeltaInControlUnit == 0m)
            return Result.Failure<StockMovement>(Error.Validation("Stock.ZeroAdjustment", "Delta de ajuste não pode ser zero."));

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        var balance = await _stockRepo.FindBySupplyItemIdAsync(supplyItemId, cancellationToken);
        if (supply is null || balance is null)
            return Result.Failure<StockMovement>(Error.NotFound("Stock.NotFound", "Saldo de estoque do insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, balance);

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var fallbackUnitCost = EstimateStockUnitCost(supply, balance.UnitCode, customUnits);
        var res = quantityDeltaInControlUnit > 0m
            ? balance.RegisterEntry(
                quantityDeltaInControlUnit,
                fallbackUnitCost,
                StockMovementType.ManualAdjustment,
                reason,
                _clock.UtcNow,
                actor.Id,
                batchNumber,
                null,
                expiresAtUtc)
            : balance.RegisterConsumption(
                Math.Abs(quantityDeltaInControlUnit),
                fallbackUnitCost,
                StockMovementType.ManualAdjustment,
                reason,
                _clock.UtcNow,
                actor.Id);

        if (res.IsFailure)
            return res;

        await _stockRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<StockMovement>> RegisterWasteOrLossAsync(
        User actor,
        Guid supplyItemId,
        decimal quantityInControlUnit,
        string reason,
        bool includeExpiredLots = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<StockMovement>(guard.Error);

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        var balance = await _stockRepo.FindBySupplyItemIdAsync(supplyItemId, cancellationToken);
        if (supply is null || balance is null)
            return Result.Failure<StockMovement>(Error.NotFound("Stock.NotFound", "Saldo de estoque do insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, balance);

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var res = balance.RegisterConsumption(
            quantityInControlUnit,
            EstimateStockUnitCost(supply, balance.UnitCode, customUnits),
            StockMovementType.LossOrWaste,
            reason,
            _clock.UtcNow,
            actor.Id,
            allowExpiredForWaste: includeExpiredLots);

        if (res.IsFailure)
            return res;

        await _stockRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    /// <summary>
    /// Executa ordem de produção com base em ficha técnica: consome ingredientes/embalagens
    /// em ordem FEFO e credita opcionalmente o lote produzido no estoque acabado.
    /// </summary>
    public async Task<Result<ProductionOrder>> ExecuteProductionOrderAsync(
        User actor,
        Guid technicalSheetId,
        decimal batchMultiplier,
        Guid? outputSupplyItemId = null,
        string? outputBatchNumber = null,
        DateTimeOffset? outputExpiresAtUtc = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<ProductionOrder>(guard.Error);

        if (batchMultiplier <= 0m)
            return Result.Failure<ProductionOrder>(Error.Validation("Production.InvalidMultiplier", "Multiplicador da receita deve ser positivo."));

        var sheet = await _sheetRepo.FindByIdAsync(technicalSheetId, cancellationToken);
        if (sheet is null)
            return Result.Failure<ProductionOrder>(Error.NotFound("TechnicalSheet.NotFound", "Ficha técnica não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, sheet);

        var requiredSupplyIds = sheet.Ingredients.Select(i => i.SupplyItemId)
            .Concat(sheet.Packagings.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value))
            .ToList();

        if (outputSupplyItemId.HasValue && outputSupplyItemId.Value != Guid.Empty)
            requiredSupplyIds.Add(outputSupplyItemId.Value);

        var distinctSupplyIds = requiredSupplyIds.Distinct().ToList();
        var supplies = await _supplyRepo.FindLookupByIdsAsync(distinctSupplyIds, cancellationToken);
        var balances = await _stockRepo.FindLookupBySupplyItemIdsAsync(distinctSupplyIds, cancellationToken);
        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var utcNow = _clock.UtcNow;

        var consumptions = new List<(Guid SupplyItemId, string SupplyName, decimal ConsumedQty, string StockUnitCode, decimal TotalCost)>();
        var itemsToConsume = sheet.Ingredients
            .Select(i => (i.SupplyItemId, Qty: i.Quantity * batchMultiplier, i.UnitCode))
            .Concat(sheet.Packagings.Where(p => p.SupplyItemId.HasValue)
                .Select(p => (SupplyItemId: p.SupplyItemId!.Value, Qty: p.Quantity * batchMultiplier, p.UnitCode)));

        foreach (var item in itemsToConsume)
        {
            if (!supplies.TryGetValue(item.SupplyItemId, out var supply))
                return Result.Failure<ProductionOrder>(Error.NotFound("Supply.NotFound", "Insumo da ficha técnica não encontrado."));

            if (!balances.TryGetValue(item.SupplyItemId, out var balance))
                return Result.Failure<ProductionOrder>(Error.NotFound("Stock.NotConfigured", $"Estoque não configurado para o insumo '{supply.Name}'."));

            TenantGuard.EnsureOwnership(_tenantContext, balance);

            var qtyInStockUnit = supply.ConvertQuantity(item.Qty, item.UnitCode, balance.UnitCode, customUnits);
            if (qtyInStockUnit.IsFailure)
                return Result.Failure<ProductionOrder>(qtyInStockUnit.Error);

            var moveRes = balance.RegisterConsumption(
                qtyInStockUnit.Value,
                EstimateStockUnitCost(supply, balance.UnitCode, customUnits),
                StockMovementType.ProductionConsumption,
                $"Produção: {sheet.Name} ({batchMultiplier}x)",
                utcNow,
                actor.Id,
                "ProductionOrder",
                null);

            if (moveRes.IsFailure)
                return Result.Failure<ProductionOrder>(moveRes.Error);

            consumptions.Add((supply.Id, supply.Name, qtyInStockUnit.Value, balance.UnitCode, moveRes.Value.TotalCost));
        }

        var orderRes = ProductionOrder.CreateCompleted(
            guard.Value,
            sheet,
            batchMultiplier,
            outputSupplyItemId,
            outputBatchNumber,
            outputExpiresAtUtc,
            actor.Id,
            utcNow,
            notes,
            consumptions);

        if (orderRes.IsFailure)
            return orderRes;

        var order = orderRes.Value;
        await _productionRepo.AddAsync(order, cancellationToken);

        if (outputSupplyItemId.HasValue && outputSupplyItemId.Value != Guid.Empty)
        {
            if (!supplies.TryGetValue(outputSupplyItemId.Value, out var outSupply) ||
                !balances.TryGetValue(outputSupplyItemId.Value, out var outBalance))
            {
                return Result.Failure<ProductionOrder>(Error.NotFound("Stock.OutputNotConfigured", "Estoque do insumo produzido não está configurado."));
            }

            TenantGuard.EnsureOwnership(_tenantContext, outBalance);

            var outQtyRes = outSupply.ConvertQuantity(order.ProducedQuantity, order.YieldUnitCode, outBalance.UnitCode, customUnits);
            if (outQtyRes.IsFailure)
                return Result.Failure<ProductionOrder>(outQtyRes.Error);

            var outUnitCost = outQtyRes.Value > 0m
                ? decimal.Round(order.TotalProductionCost / outQtyRes.Value, 4, MidpointRounding.ToEven)
                : 0m;

            var entryRes = outBalance.RegisterEntry(
                outQtyRes.Value,
                outUnitCost,
                StockMovementType.ProductionOutput,
                $"Entrada produção: {sheet.Name}",
                utcNow,
                actor.Id,
                order.BatchNumber,
                utcNow,
                order.ExpiresAtUtc,
                "ProductionOrder",
                order.Id);

            if (entryRes.IsFailure)
                return Result.Failure<ProductionOrder>(entryRes.Error);
        }

        await _productionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(order);
    }

    public async Task<Result<StockOrderDeductionSummary>> DeductForOrderSaleAsync(
        User actor,
        Guid orderId,
        string orderReferenceLabel,
        IEnumerable<OrderStockLineInput> orderLines,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateOrderTriggeredActor(actor, isCancellation: false);
        if (guard.IsFailure)
            return Result.Failure<StockOrderDeductionSummary>(guard.Error);

        if (orderId == Guid.Empty)
            return Result.Failure<StockOrderDeductionSummary>(Error.Validation("Order.InvalidId", "OrderId é obrigatório."));

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var utcNow = _clock.UtcNow;
        var movementsCreated = 0;
        var skippedItems = 0;
        var lineIndex = 0;

        foreach (var line in orderLines)
        {
            if (line.Quantity <= 0m)
            {
                lineIndex++;
                continue;
            }

            var sheet = await _sheetRepo.FindActiveByProductIdAsync(line.ProductId, cancellationToken);
            if (sheet is null)
            {
                skippedItems++;
                lineIndex++;
                continue;
            }

            TenantGuard.EnsureOwnership(_tenantContext, sheet);

            var lineInSheetYieldRes = UnitConverter.Convert(line.Quantity, line.UnitCode, sheet.YieldUnitCode, customUnits);
            if (lineInSheetYieldRes.IsFailure)
                return Result.Failure<StockOrderDeductionSummary>(lineInSheetYieldRes.Error);

            var batchScale = lineInSheetYieldRes.Value / sheet.YieldQuantity;
            var supplyIds = sheet.Ingredients.Select(i => i.SupplyItemId)
                .Concat(sheet.Packagings.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value))
                .Distinct()
                .ToList();

            var supplies = await _supplyRepo.FindLookupByIdsAsync(supplyIds, cancellationToken);
            var balances = await _stockRepo.FindLookupBySupplyItemIdsAsync(supplyIds, cancellationToken);

            var components = sheet.Ingredients
                .Select(i => (i.SupplyItemId, Qty: i.Quantity * batchScale, i.UnitCode))
                .Concat(sheet.Packagings.Where(p => p.SupplyItemId.HasValue)
                    .Select(p => (SupplyItemId: p.SupplyItemId!.Value, Qty: p.Quantity * batchScale, p.UnitCode)))
                .ToList();

            for (var compIndex = 0; compIndex < components.Count; compIndex++)
            {
                var comp = components[compIndex];
                if (!supplies.TryGetValue(comp.SupplyItemId, out var supply) ||
                    !balances.TryGetValue(comp.SupplyItemId, out var balance) ||
                    !balance.AutoDeductOnOrder)
                {
                    skippedItems++;
                    continue;
                }

                TenantGuard.EnsureOwnership(_tenantContext, balance);

                var idempotencyKey = $"order-sale:{orderId:N}:{lineIndex}:{compIndex}:{comp.SupplyItemId:N}";
                if (balance.HasMovementWithKey(idempotencyKey))
                    continue;

                var qtyInStockRes = supply.ConvertQuantity(comp.Qty, comp.UnitCode, balance.UnitCode, customUnits);
                if (qtyInStockRes.IsFailure)
                    return Result.Failure<StockOrderDeductionSummary>(qtyInStockRes.Error);

                var moveRes = balance.RegisterConsumption(
                    qtyInStockRes.Value,
                    EstimateStockUnitCost(supply, balance.UnitCode, customUnits),
                    StockMovementType.OrderSaleDeduction,
                    $"Baixa automática venda {orderReferenceLabel}",
                    utcNow,
                    actor.Id,
                    "Order",
                    orderId,
                    idempotencyKey);

                if (moveRes.IsFailure)
                    return Result.Failure<StockOrderDeductionSummary>(moveRes.Error);

                movementsCreated++;
            }

            lineIndex++;
        }

        if (movementsCreated > 0)
            await _stockRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new StockOrderDeductionSummary(movementsCreated, skippedItems));
    }

    public async Task<Result<int>> ReverseOrderDeductionAsync(
        User actor,
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateOrderTriggeredActor(actor, isCancellation: true);
        if (guard.IsFailure)
            return Result.Failure<int>(guard.Error);

        var saleMovements = (await _stockRepo.ListByReferenceAsync("Order", orderId, cancellationToken))
            .Where(m => m.MovementType == StockMovementType.OrderSaleDeduction && m.QuantityDelta < 0m)
            .ToList();

        if (saleMovements.Count == 0)
            return Result.Success(0);

        var balances = await _stockRepo.FindLookupBySupplyItemIdsAsync(
            saleMovements.Select(m => m.SupplyItemId).Distinct(),
            cancellationToken);

        var utcNow = _clock.UtcNow;
        var reversedCount = 0;

        foreach (var movement in saleMovements)
        {
            if (!balances.TryGetValue(movement.SupplyItemId, out var balance))
                continue;

            TenantGuard.EnsureOwnership(_tenantContext, balance);

            var reversalKey = $"order-cancel:{orderId:N}:{movement.Id:N}";
            if (balance.HasMovementWithKey(reversalKey))
                continue;

            var entryRes = balance.RegisterEntry(
                Math.Abs(movement.QuantityDelta),
                movement.UnitCost,
                StockMovementType.OrderCancellationReversal,
                reason,
                utcNow,
                actor.Id,
                referenceType: "Order",
                referenceId: orderId,
                idempotencyKey: reversalKey);

            if (entryRes.IsFailure)
                return Result.Failure<int>(entryRes.Error);

            reversedCount++;
        }

        if (reversedCount > 0)
            await _stockRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(reversedCount);
    }

    public async Task<Result<InventoryAlertsSnapshot>> GetActiveAlertsAsync(
        User actor,
        int expiringWithinDays = 7,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<InventoryAlertsSnapshot>(guard.Error);

        var balances = await _stockRepo.ListAllWithLotsAsync(cancellationToken);
        var supplies = await _supplyRepo.FindLookupByIdsAsync(balances.Select(b => b.SupplyItemId), cancellationToken);
        var utcNow = _clock.UtcNow;

        var lowStock = new List<LowStockAlertItem>();
        var expired = new List<LotExpiryAlertItem>();
        var expiringSoon = new List<LotExpiryAlertItem>();

        foreach (var balance in balances)
        {
            var supplyName = supplies.TryGetValue(balance.SupplyItemId, out var s) ? s.Name : "Insumo";

            if (balance.IsBelowMinimum())
            {
                lowStock.Add(new LowStockAlertItem(
                    balance.SupplyItemId,
                    supplyName,
                    balance.UnitCode,
                    balance.QuantityOnHand,
                    balance.MinimumStockQuantity));
            }

            foreach (var lot in balance.Lots.Where(l => l.RemainingQuantity > 0m && l.ExpiresAtUtc.HasValue))
            {
                if (lot.IsExpired(utcNow))
                {
                    expired.Add(new LotExpiryAlertItem(
                        lot.Id,
                        balance.SupplyItemId,
                        supplyName,
                        lot.BatchNumber,
                        lot.RemainingQuantity,
                        lot.UnitCode,
                        lot.ExpiresAtUtc!.Value,
                        true));
                }
                else if (lot.IsExpiringWithin(utcNow, expiringWithinDays))
                {
                    expiringSoon.Add(new LotExpiryAlertItem(
                        lot.Id,
                        balance.SupplyItemId,
                        supplyName,
                        lot.BatchNumber,
                        lot.RemainingQuantity,
                        lot.UnitCode,
                        lot.ExpiresAtUtc!.Value,
                        false));
                }
            }
        }

        return Result.Success(new InventoryAlertsSnapshot(lowStock, expired, expiringSoon));
    }

    private static decimal EstimateStockUnitCost(
        SupplyItem supply,
        string stockUnitCode,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits)
    {
        var oneStockUnitCost = supply.CalculateCostForUsage(1m, stockUnitCode, customUnits);
        return oneStockUnitCost.IsSuccess ? oneStockUnitCost.Value : 0m;
    }

    private Result<Guid> ValidateOrderTriggeredActor(User actor, bool isCancellation)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar estoque via pedido de outro estabelecimento.");

        var allowed = actor.HasPermission(SystemModules.Inventory, PermissionAction.Edit) ||
                      (isCancellation
                          ? actor.HasPermission(SystemModules.Orders, PermissionAction.Cancel)
                          : actor.HasPermission(SystemModules.Orders, PermissionAction.Create) || actor.HasPermission(SystemModules.Orders, PermissionAction.Edit));

        return allowed
            ? Result.Success(currentTenantId)
            : Result.Failure<Guid>(Error.Forbidden("Inventory.Forbidden", "Permissão insuficiente para movimentação automática de estoque via pedido."));
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar estoque/produção de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Inventory, action))
            return Result.Failure<Guid>(Error.Forbidden("Inventory.Forbidden", "Permissão insuficiente para operar estoque, compras ou produção."));

        return Result.Success(currentTenantId);
    }
}
