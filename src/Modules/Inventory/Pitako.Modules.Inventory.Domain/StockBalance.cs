using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Inventory.Domain;

/// <summary>
/// Tipos oficiais de movimentação de estoque para auditoria e conciliação (Seção 16).
/// </summary>
public enum StockMovementType
{
    PurchaseEntry = 1,
    ManualAdjustment = 2,
    LossOrWaste = 3,
    ProductionConsumption = 4,
    ProductionOutput = 5,
    OrderSaleDeduction = 6,
    OrderCancellationReversal = 7,
    InventoryCount = 8
}

/// <summary>
/// Lote com rastreabilidade e controle opcional de validade para consumo em ordem FEFO (Seção 16).
/// </summary>
public sealed class StockLot : Entity<Guid>, ITenantScopedEntity
{
    private StockLot()
    {
        BatchNumber = string.Empty;
        UnitCode = "unidade";
    }

    internal StockLot(
        Guid id,
        Guid tenantId,
        Guid stockBalanceId,
        Guid supplyItemId,
        string batchNumber,
        decimal initialQuantity,
        string unitCode,
        decimal unitCost,
        DateTimeOffset? manufacturedAtUtc,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        StockBalanceId = stockBalanceId;
        SupplyItemId = supplyItemId;
        BatchNumber = batchNumber;
        InitialQuantity = decimal.Round(initialQuantity, 4, MidpointRounding.ToEven);
        RemainingQuantity = InitialQuantity;
        UnitCode = unitCode;
        UnitCost = decimal.Round(unitCost, 4, MidpointRounding.ToEven);
        ManufacturedAtUtc = manufacturedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid StockBalanceId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public string BatchNumber { get; private set; }
    public decimal InitialQuantity { get; private set; }
    public decimal RemainingQuantity { get; private set; }
    public string UnitCode { get; private set; }
    public decimal UnitCost { get; private set; }
    public DateTimeOffset? ManufacturedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));

        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em StockLot.");

        TenantId = tenantId;
    }

    public bool IsExpired(DateTimeOffset utcNow) =>
        ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= utcNow;

    public bool IsExpiringWithin(DateTimeOffset utcNow, int days) =>
        ExpiresAtUtc.HasValue && ExpiresAtUtc.Value > utcNow && ExpiresAtUtc.Value <= utcNow.AddDays(days);

    internal decimal ConsumeUpTo(decimal requestedQuantity)
    {
        if (RemainingQuantity <= 0m || requestedQuantity <= 0m)
            return 0m;

        var taken = Math.Min(RemainingQuantity, requestedQuantity);
        RemainingQuantity = decimal.Round(RemainingQuantity - taken, 4, MidpointRounding.ToEven);
        return taken;
    }

    internal void Replenish(decimal quantity)
    {
        RemainingQuantity = decimal.Round(RemainingQuantity + quantity, 4, MidpointRounding.ToEven);
    }
}

/// <summary>
/// Registro histórico imutável de entrada, saída, perda, produção ou estorno de estoque (Seção 16).
/// </summary>
public sealed class StockMovement : Entity<Guid>, ITenantScopedEntity
{
    private StockMovement()
    {
        UnitCode = "unidade";
        Reason = string.Empty;
    }

    internal StockMovement(
        Guid id,
        Guid tenantId,
        Guid stockBalanceId,
        Guid supplyItemId,
        Guid? stockLotId,
        StockMovementType movementType,
        decimal quantityDelta,
        decimal previousQuantity,
        decimal resultingQuantity,
        string unitCode,
        decimal unitCost,
        string reason,
        string? referenceType,
        Guid? referenceId,
        string? idempotencyKey,
        Guid? actorUserId,
        DateTimeOffset occurredAtUtc,
        decimal? explicitTotalCost = null) : base(id)
    {
        TenantId = tenantId;
        StockBalanceId = stockBalanceId;
        SupplyItemId = supplyItemId;
        StockLotId = stockLotId;
        MovementType = movementType;
        QuantityDelta = decimal.Round(quantityDelta, 4, MidpointRounding.ToEven);
        PreviousQuantity = decimal.Round(previousQuantity, 4, MidpointRounding.ToEven);
        ResultingQuantity = decimal.Round(resultingQuantity, 4, MidpointRounding.ToEven);
        UnitCode = unitCode;
        UnitCost = decimal.Round(unitCost, 4, MidpointRounding.ToEven);
        TotalCost = decimal.Round(explicitTotalCost ?? (Math.Abs(QuantityDelta) * UnitCost), 4, MidpointRounding.ToEven);
        Reason = reason;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        IdempotencyKey = idempotencyKey;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));

        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em StockMovement.");

        TenantId = tenantId;
    }

    public Guid TenantId { get; private set; }
    public Guid StockBalanceId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid? StockLotId { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public decimal PreviousQuantity { get; private set; }
    public decimal ResultingQuantity { get; private set; }
    public string UnitCode { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public string Reason { get; private set; }
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}

/// <summary>
/// Posição consolidada de estoque por insumo, com política de estoque mínimo,
/// lotes/validade FEFO, permissão configurável de saldo negativo e baixa automática em vendas (Seção 16).
/// </summary>
public sealed class StockBalance : TenantScopedAggregateRoot
{
    private readonly List<StockLot> _lots = new();
    private readonly List<StockMovement> _movements = new();

    private StockBalance()
    {
        UnitCode = "unidade";
    }

    private StockBalance(
        Guid id,
        Guid tenantId,
        Guid supplyItemId,
        string unitCode,
        decimal minimumStockQuantity,
        bool allowNegativeStock,
        bool autoDeductOnOrder,
        bool blockExpiredLots) : base(id, tenantId)
    {
        SupplyItemId = supplyItemId;
        UnitCode = unitCode;
        QuantityOnHand = 0m;
        MinimumStockQuantity = decimal.Round(minimumStockQuantity, 4, MidpointRounding.ToEven);
        AllowNegativeStock = allowNegativeStock;
        AutoDeductOnOrder = autoDeductOnOrder;
        BlockExpiredLots = blockExpiredLots;
    }

    public Guid SupplyItemId { get; private set; }
    public string UnitCode { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public decimal MinimumStockQuantity { get; private set; }
    public bool AllowNegativeStock { get; private set; }
    public bool AutoDeductOnOrder { get; private set; }
    public bool BlockExpiredLots { get; private set; }

    public IReadOnlyCollection<StockLot> Lots => _lots.AsReadOnly();
    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    public static Result<StockBalance> Create(
        Guid tenantId,
        SupplyItem supplyItem,
        string controlUnitCode,
        decimal minimumStockQuantity = 0m,
        bool allowNegativeStock = false,
        bool autoDeductOnOrder = true,
        bool blockExpiredLots = true,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits = null)
    {
        ArgumentNullException.ThrowIfNull(supplyItem);

        if (tenantId == Guid.Empty)
            return Result.Failure<StockBalance>(Error.Validation("Stock.TenantRequired", "TenantId é obrigatório."));

        if (supplyItem.TenantId != tenantId)
            return Result.Failure<StockBalance>(Error.Forbidden("Stock.CrossTenantSupply", "Insumo pertence a outro estabelecimento."));

        if (minimumStockQuantity < 0m)
            return Result.Failure<StockBalance>(Error.Validation("Stock.NegativeMinimum", "Estoque mínimo não pode ser negativo."));

        var unitCheck = supplyItem.ConvertQuantity(1m, supplyItem.ContentUnitCode, controlUnitCode, customUnits);
        if (unitCheck.IsFailure)
            return Result.Failure<StockBalance>(unitCheck.Error);

        var normalizedUnit = UnitConverter.NormalizeCode(controlUnitCode);
        return Result.Success(new StockBalance(
            Guid.NewGuid(),
            tenantId,
            supplyItem.Id,
            normalizedUnit,
            minimumStockQuantity,
            allowNegativeStock,
            autoDeductOnOrder,
            blockExpiredLots));
    }

    public Result UpdatePolicy(
        decimal minimumStockQuantity,
        bool allowNegativeStock,
        bool autoDeductOnOrder,
        bool blockExpiredLots)
    {
        if (minimumStockQuantity < 0m)
            return Result.Failure(Error.Validation("Stock.NegativeMinimum", "Estoque mínimo não pode ser negativo."));

        MinimumStockQuantity = decimal.Round(minimumStockQuantity, 4, MidpointRounding.ToEven);
        AllowNegativeStock = allowNegativeStock;
        AutoDeductOnOrder = autoDeductOnOrder;
        BlockExpiredLots = blockExpiredLots;
        return Result.Success();
    }

    public bool IsBelowMinimum() => MinimumStockQuantity > 0m && QuantityOnHand <= MinimumStockQuantity;

    public bool HasMovementWithKey(string? idempotencyKey) =>
        !string.IsNullOrWhiteSpace(idempotencyKey) &&
        _movements.Any(m => string.Equals(m.IdempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// Registra entrada física no estoque (compra, produção ou ajuste positivo) com criação opcional de lote.
    /// </summary>
    public Result<StockMovement> RegisterEntry(
        decimal quantityInControlUnit,
        decimal unitCost,
        StockMovementType movementType,
        string reason,
        DateTimeOffset utcNow,
        Guid? actorUserId,
        string? batchNumber = null,
        DateTimeOffset? manufacturedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        string? referenceType = null,
        Guid? referenceId = null,
        string? idempotencyKey = null)
    {
        if (quantityInControlUnit <= 0m)
            return Result.Failure<StockMovement>(Error.Validation("Stock.InvalidEntryQty", "Quantidade de entrada deve ser positiva."));

        if (unitCost < 0m)
            return Result.Failure<StockMovement>(Error.Validation("Stock.NegativeCost", "Custo unitário não pode ser negativo."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<StockMovement>(Error.Validation("Stock.ReasonRequired", "Motivo da movimentação é obrigatório."));

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existing = _movements.FirstOrDefault(m => string.Equals(m.IdempotencyKey, normalizedKey, StringComparison.Ordinal));
            if (existing is not null)
                return Result.Success(existing);
        }

        Guid? createdLotId = null;
        if (!string.IsNullOrWhiteSpace(batchNumber) || expiresAtUtc.HasValue)
        {
            var lotCode = string.IsNullOrWhiteSpace(batchNumber)
                ? $"LOT-{utcNow:yyyyMMddHHmmss}"
                : batchNumber.Trim().ToUpperInvariant();

            var lot = new StockLot(
                Guid.NewGuid(),
                TenantId,
                Id,
                SupplyItemId,
                lotCode,
                quantityInControlUnit,
                UnitCode,
                unitCost,
                manufacturedAtUtc,
                expiresAtUtc,
                utcNow);
            _lots.Add(lot);
            createdLotId = lot.Id;
        }

        var previous = QuantityOnHand;
        var roundedQty = decimal.Round(quantityInControlUnit, 4, MidpointRounding.ToEven);
        QuantityOnHand = decimal.Round(previous + roundedQty, 4, MidpointRounding.ToEven);

        var movement = new StockMovement(
            Guid.NewGuid(),
            TenantId,
            Id,
            SupplyItemId,
            createdLotId,
            movementType,
            roundedQty,
            previous,
            QuantityOnHand,
            UnitCode,
            unitCost,
            reason.Trim(),
            referenceType,
            referenceId,
            normalizedKey,
            actorUserId,
            utcNow);

        _movements.Add(movement);
        return Result.Success(movement);
    }

    /// <summary>
    /// Registra baixa no estoque em ordem FEFO (First-Expired, First-Out) respeitando lotes vencidos e política de saldo negativo.
    /// </summary>
    public Result<StockMovement> RegisterConsumption(
        decimal quantityInControlUnit,
        decimal fallbackUnitCost,
        StockMovementType movementType,
        string reason,
        DateTimeOffset utcNow,
        Guid? actorUserId,
        string? referenceType = null,
        Guid? referenceId = null,
        string? idempotencyKey = null,
        bool allowExpiredForWaste = false)
    {
        if (quantityInControlUnit <= 0m)
            return Result.Failure<StockMovement>(Error.Validation("Stock.InvalidExitQty", "Quantidade de saída deve ser positiva."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<StockMovement>(Error.Validation("Stock.ReasonRequired", "Motivo da movimentação é obrigatório."));

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existing = _movements.FirstOrDefault(m => string.Equals(m.IdempotencyKey, normalizedKey, StringComparison.Ordinal));
            if (existing is not null)
                return Result.Success(existing);
        }

        var roundedRequested = decimal.Round(quantityInControlUnit, 4, MidpointRounding.ToEven);
        var resulting = decimal.Round(QuantityOnHand - roundedRequested, 4, MidpointRounding.ToEven);

        if (!AllowNegativeStock && resulting < 0m)
        {
            return Result.Failure<StockMovement>(Error.Conflict(
                "Stock.InsufficientBalance",
                $"Saldo insuficiente ({QuantityOnHand:N4} {UnitCode}) para baixar {roundedRequested:N4} {UnitCode}."));
        }

        // Seleciona lotes elegíveis em ordem FEFO (menor validade primeiro; sem validade ao final).
        var candidateLots = _lots
            .Where(l => l.RemainingQuantity > 0m)
            .OrderBy(l => l.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(l => l.CreatedAtUtc)
            .ToList();

        if (!allowExpiredForWaste && BlockExpiredLots && candidateLots.Count > 0)
        {
            var nonExpiredAvailable = candidateLots
                .Where(l => !l.IsExpired(utcNow))
                .Sum(l => l.RemainingQuantity);

            var unlottedPositive = Math.Max(0m, QuantityOnHand - candidateLots.Sum(l => l.RemainingQuantity));
            if (!AllowNegativeStock && nonExpiredAvailable + unlottedPositive < roundedRequested)
            {
                return Result.Failure<StockMovement>(Error.Conflict(
                    "Stock.OnlyExpiredLotsAvailable",
                    "Estoque disponível insuficiente em lotes não vencidos."));
            }

            candidateLots = candidateLots.Where(l => !l.IsExpired(utcNow)).ToList();
        }

        var remainingToDrain = roundedRequested;
        decimal totalWeightedCost = 0m;
        Guid? primaryLotId = null;

        foreach (var lot in candidateLots)
        {
            if (remainingToDrain <= 0m)
                break;

            var drained = lot.ConsumeUpTo(remainingToDrain);
            if (drained > 0m)
            {
                primaryLotId ??= lot.Id;
                totalWeightedCost += drained * lot.UnitCost;
                remainingToDrain = decimal.Round(remainingToDrain - drained, 4, MidpointRounding.ToEven);
            }
        }

        if (remainingToDrain > 0m)
            totalWeightedCost += remainingToDrain * Math.Max(0m, fallbackUnitCost);

        var effectiveUnitCost = decimal.Round(totalWeightedCost / roundedRequested, 4, MidpointRounding.ToEven);
        var previous = QuantityOnHand;
        QuantityOnHand = resulting;

        var movement = new StockMovement(
            Guid.NewGuid(),
            TenantId,
            Id,
            SupplyItemId,
            primaryLotId,
            movementType,
            -roundedRequested,
            previous,
            QuantityOnHand,
            UnitCode,
            effectiveUnitCost,
            reason.Trim(),
            referenceType,
            referenceId,
            normalizedKey,
            actorUserId,
            utcNow,
            totalWeightedCost);

        _movements.Add(movement);
        return Result.Success(movement);
    }
}
