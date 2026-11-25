using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Domain;

/// <summary>
/// Linha de entrada de mercadoria convertida da unidade comercial de compra para a unidade de estoque (Seção 16).
/// </summary>
public sealed class PurchaseReceiptItem : Entity<Guid>, ITenantScopedEntity
{
    private PurchaseReceiptItem()
    {
        SupplyNameSnapshot = string.Empty;
        PurchaseUnitCode = "unidade";
        StockUnitCode = "unidade";
        UnitPrice = Money.Zero();
        TotalCost = Money.Zero();
    }

    internal PurchaseReceiptItem(
        Guid id,
        Guid tenantId,
        Guid purchaseReceiptId,
        Guid supplyItemId,
        string supplyNameSnapshot,
        decimal purchasedQuantity,
        string purchaseUnitCode,
        decimal creditedStockQuantity,
        string stockUnitCode,
        Money unitPrice,
        Money totalCost,
        string? batchNumber,
        DateTimeOffset? manufacturedAtUtc,
        DateTimeOffset? expiresAtUtc) : base(id)
    {
        TenantId = tenantId;
        PurchaseReceiptId = purchaseReceiptId;
        SupplyItemId = supplyItemId;
        SupplyNameSnapshot = supplyNameSnapshot;
        PurchasedQuantity = decimal.Round(purchasedQuantity, 4, MidpointRounding.ToEven);
        PurchaseUnitCode = purchaseUnitCode;
        CreditedStockQuantity = decimal.Round(creditedStockQuantity, 4, MidpointRounding.ToEven);
        StockUnitCode = stockUnitCode;
        UnitPrice = Money.Create(unitPrice.Amount, unitPrice.Currency).Value;
        TotalCost = Money.Create(totalCost.Amount, totalCost.Currency).Value;
        BatchNumber = batchNumber;
        ManufacturedAtUtc = manufacturedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid PurchaseReceiptId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public string SupplyNameSnapshot { get; private set; }
    public decimal PurchasedQuantity { get; private set; }
    public string PurchaseUnitCode { get; private set; }
    public decimal CreditedStockQuantity { get; private set; }
    public string StockUnitCode { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money TotalCost { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateTimeOffset? ManufacturedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));

        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em PurchaseReceiptItem.");

        TenantId = tenantId;
    }
}

public sealed record PurchaseLineInput(
    Guid SupplyItemId,
    decimal PurchasedQuantity,
    string PurchaseUnitCode,
    decimal UnitPriceAmount,
    string? BatchNumber = null,
    DateTimeOffset? ManufacturedAtUtc = null,
    DateTimeOffset? ExpiresAtUtc = null);

/// <summary>
/// Registro de entrada de compras/notas fiscais de fornecedores com auditoria de custo e conversão automática (Seção 16).
/// </summary>
public sealed class PurchaseReceipt : TenantScopedAggregateRoot
{
    private readonly List<PurchaseReceiptItem> _items = new();

    private PurchaseReceipt()
    {
        SupplierName = string.Empty;
        TotalAmount = Money.Zero();
    }

    private PurchaseReceipt(
        Guid id,
        Guid tenantId,
        string supplierName,
        string? invoiceNumber,
        string? notes,
        Guid? receivedByUserId,
        DateTimeOffset receivedAtUtc,
        Money totalAmount,
        string? idempotencyKey) : base(id, tenantId)
    {
        SupplierName = supplierName;
        InvoiceNumber = invoiceNumber;
        Notes = notes;
        ReceivedByUserId = receivedByUserId;
        ReceivedAtUtc = receivedAtUtc;
        TotalAmount = totalAmount;
        IdempotencyKey = idempotencyKey;
    }

    public string SupplierName { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ReceivedByUserId { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public Money TotalAmount { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public IReadOnlyCollection<PurchaseReceiptItem> Items => _items.AsReadOnly();

    public static Result<PurchaseReceipt> Create(
        Guid tenantId,
        string supplierName,
        string? invoiceNumber,
        string? notes,
        Guid? receivedByUserId,
        DateTimeOffset receivedAtUtc,
        IEnumerable<(PurchaseLineInput Line, SupplyItem Supply, decimal CreditedStockQty, string StockUnitCode)> resolvedLines,
        string? idempotencyKey = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(supplierName))
            return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.SupplierRequired", "Fornecedor é obrigatório."));

        var lines = resolvedLines.ToList();
        if (lines.Count == 0)
            return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.EmptyItems", "Entrada de compra exige ao menos 1 item."));

        var receiptId = Guid.NewGuid();
        var itemEntities = new List<PurchaseReceiptItem>();
        decimal totalSum = 0m;

        foreach (var (line, supply, creditedQty, stockUnit) in lines)
        {
            if (line.PurchasedQuantity <= 0m)
                return Result.Failure<PurchaseReceipt>(Error.Validation("Purchase.InvalidQty", "Quantidade comprada deve ser positiva."));

            var unitPriceRes = Money.Create(line.UnitPriceAmount);
            if (unitPriceRes.IsFailure)
                return Result.Failure<PurchaseReceipt>(unitPriceRes.Error);

            var lineTotalAmount = decimal.Round(line.UnitPriceAmount * line.PurchasedQuantity, 2, MidpointRounding.ToEven);
            var lineTotalRes = Money.Create(lineTotalAmount);
            if (lineTotalRes.IsFailure)
                return Result.Failure<PurchaseReceipt>(lineTotalRes.Error);

            totalSum += lineTotalAmount;

            itemEntities.Add(new PurchaseReceiptItem(
                Guid.NewGuid(),
                tenantId,
                receiptId,
                supply.Id,
                supply.Name,
                line.PurchasedQuantity,
                UnitConverter.NormalizeCode(line.PurchaseUnitCode),
                creditedQty,
                stockUnit,
                unitPriceRes.Value,
                lineTotalRes.Value,
                string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim().ToUpperInvariant(),
                line.ManufacturedAtUtc,
                line.ExpiresAtUtc));
        }

        var receipt = new PurchaseReceipt(
            receiptId,
            tenantId,
            supplierName.Trim(),
            string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            receivedByUserId,
            receivedAtUtc,
            Money.Create(totalSum).Value,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim());

        receipt._items.AddRange(itemEntities);
        return Result.Success(receipt);
    }
}
