using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Orders.Domain;

public enum OrderChannel
{
    OwnDeliveryMenu = 1,
    PdvCounter = 2,
    TableWaiter = 3,
    QrCodeTable = 4,
    SelfServiceTotem = 5,
    WhatsApp = 6,
    Marketplace = 7
}

public enum FulfillmentMode
{
    Delivery = 1,
    CounterPickup = 2,
    DineIn = 3
}

public enum OrderStatus
{
    Received = 1,
    Accepted = 2,
    InPreparation = 3,
    Ready = 4,
    Dispatched = 5,
    Completed = 6,
    Cancelled = 7
}

/// <summary>
/// Linha imutável de item do pedido preservando escolhas, adicionais, copos personalizados e pesagem (Seções 17 e 21).
/// </summary>
public sealed class OrderItem : Entity<Guid>, ITenantScopedEntity
{
    private OrderItem()
    {
        ItemTitle = string.Empty;
        CustomizationSummaryText = string.Empty;
        SelectionsPayloadJson = "{}";
        UnitPrice = Money.Zero();
        LineSubtotal = Money.Zero();
    }

    internal OrderItem(
        Guid id,
        Guid tenantId,
        Guid orderId,
        CartItemKind itemKind,
        Guid? productId,
        Guid? categoryId,
        Guid? variationId,
        Guid? acaiCupSizeId,
        Guid? weighingRecordId,
        string itemTitle,
        string customizationSummaryText,
        string selectionsPayloadJson,
        Money unitPrice,
        int quantity,
        Money lineSubtotal,
        string? customerNotes) : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        ItemKind = itemKind;
        ProductId = productId;
        CategoryId = categoryId;
        VariationId = variationId;
        AcaiCupSizeId = acaiCupSizeId;
        WeighingRecordId = weighingRecordId;
        ItemTitle = itemTitle;
        CustomizationSummaryText = customizationSummaryText;
        SelectionsPayloadJson = selectionsPayloadJson;
        UnitPrice = Money.Create(unitPrice.Amount, unitPrice.Currency).Value;
        Quantity = quantity;
        LineSubtotal = Money.Create(lineSubtotal.Amount, lineSubtotal.Currency).Value;
        CustomerNotes = customerNotes;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public CartItemKind ItemKind { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? VariationId { get; private set; }
    public Guid? AcaiCupSizeId { get; private set; }
    public Guid? WeighingRecordId { get; private set; }
    public string ItemTitle { get; private set; }
    public string CustomizationSummaryText { get; private set; }
    public string SelectionsPayloadJson { get; private set; }
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineSubtotal { get; private set; }
    public string? CustomerNotes { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em OrderItem.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Linha auditável de transição no painel Kanban do pedido (Seção 21).
/// </summary>
public sealed class OrderStatusHistory : Entity<Guid>, ITenantScopedEntity
{
    private OrderStatusHistory()
    {
        Reason = string.Empty;
    }

    internal OrderStatusHistory(
        Guid id,
        Guid tenantId,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        Guid? actorUserId,
        string reason,
        DateTimeOffset changedAtUtc) : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = actorUserId;
        Reason = reason;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em OrderStatusHistory.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Agregado raiz de Pedido com máquina de estados Kanban, preservação integral de personalizações,
/// entrega/retirada/mesa, cupom aplicado, baixa de estoque e trilha de auditoria (Seções 17, 20 e 21).
/// </summary>
public sealed class Order : TenantScopedAggregateRoot
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusHistory> _statusHistory = new();

    private Order()
    {
        OrderNumber = string.Empty;
        CustomerName = string.Empty;
        ItemsSubtotal = Money.Zero();
        ItemsDiscount = Money.Zero();
        DeliveryFee = Money.Zero();
        DeliveryDiscount = Money.Zero();
        NetDeliveryFee = Money.Zero();
        TotalAmount = Money.Zero();
    }

    private Order(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string orderNumber,
        OrderChannel channel,
        FulfillmentMode fulfillmentMode,
        string customerName,
        string? customerIdentifier,
        string? customerPhone,
        string? deliveryAddressSummary,
        string? appliedCouponCode,
        Money itemsSubtotal,
        Money itemsDiscount,
        Money deliveryFee,
        Money deliveryDiscount,
        string? idempotencyKey,
        DateTimeOffset placedAtUtc,
        Guid? createdByUserId) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        OrderNumber = orderNumber;
        Channel = channel;
        FulfillmentMode = fulfillmentMode;
        Status = OrderStatus.Received;
        CustomerName = customerName;
        CustomerIdentifier = customerIdentifier;
        CustomerPhone = customerPhone;
        DeliveryAddressSummary = deliveryAddressSummary;
        AppliedCouponCode = appliedCouponCode;
        ItemsSubtotal = Money.Create(itemsSubtotal.Amount, itemsSubtotal.Currency).Value;
        ItemsDiscount = Money.Create(itemsDiscount.Amount, itemsDiscount.Currency).Value;
        DeliveryFee = Money.Create(deliveryFee.Amount, deliveryFee.Currency).Value;
        DeliveryDiscount = Money.Create(deliveryDiscount.Amount, deliveryDiscount.Currency).Value;

        var netDelivery = Math.Max(0m, deliveryFee.Amount - deliveryDiscount.Amount);
        var netItems = Math.Max(0m, itemsSubtotal.Amount - itemsDiscount.Amount);
        NetDeliveryFee = Money.Create(netDelivery, deliveryFee.Currency).Value;
        TotalAmount = Money.Create(netItems + netDelivery, itemsSubtotal.Currency).Value;

        IdempotencyKey = idempotencyKey;
        PlacedAtUtc = placedAtUtc;

        _statusHistory.Add(new OrderStatusHistory(
            Guid.NewGuid(),
            tenantId,
            id,
            null,
            OrderStatus.Received,
            createdByUserId,
            "Pedido registrado",
            placedAtUtc));
    }

    public Guid? StoreUnitId { get; private set; }
    public string OrderNumber { get; private set; }
    public OrderChannel Channel { get; private set; }
    public FulfillmentMode FulfillmentMode { get; private set; }
    public OrderStatus Status { get; private set; }
    public string CustomerName { get; private set; }
    public string? CustomerIdentifier { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? DeliveryAddressSummary { get; private set; }
    public string? AppliedCouponCode { get; private set; }
    public Money ItemsSubtotal { get; private set; }
    public Money ItemsDiscount { get; private set; }
    public Money DeliveryFee { get; private set; }
    public Money DeliveryDiscount { get; private set; }
    public Money NetDeliveryFee { get; private set; }
    public Money TotalAmount { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset PlacedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    public static Result<Order> CreateFromCart(
        ShoppingCart cart,
        string orderNumber,
        OrderChannel channel,
        FulfillmentMode fulfillmentMode,
        string customerName,
        string? customerPhone,
        string? deliveryAddressSummary,
        Money authoritativeDeliveryFee,
        Money authoritativeDeliveryDiscount,
        Money authoritativeItemsDiscount,
        string? appliedCouponCode,
        Guid? storeUnitId,
        Guid? actorUserId,
        string? idempotencyKey,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(authoritativeDeliveryFee);
        ArgumentNullException.ThrowIfNull(authoritativeDeliveryDiscount);
        ArgumentNullException.ThrowIfNull(authoritativeItemsDiscount);

        if (cart.Items.Count == 0)
            return Result.Failure<Order>(Error.Validation("Order.EmptyCart", "Não é possível finalizar pedido com carrinho vazio."));
        if (string.IsNullOrWhiteSpace(orderNumber))
            return Result.Failure<Order>(Error.Validation("Order.NumberRequired", "Número do pedido é obrigatório."));
        if (string.IsNullOrWhiteSpace(customerName))
            return Result.Failure<Order>(Error.Validation("Order.CustomerNameRequired", "Nome do cliente é obrigatório."));
        if (fulfillmentMode == FulfillmentMode.Delivery && string.IsNullOrWhiteSpace(deliveryAddressSummary))
            return Result.Failure<Order>(Error.Validation("Order.AddressRequired", "Endereço de entrega é obrigatório para delivery."));

        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            cart.TenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            orderNumber.Trim().ToUpperInvariant(),
            channel,
            fulfillmentMode,
            customerName.Trim(),
            cart.CustomerIdentifier,
            string.IsNullOrWhiteSpace(customerPhone) ? null : customerPhone.Trim(),
            string.IsNullOrWhiteSpace(deliveryAddressSummary) ? null : deliveryAddressSummary.Trim(),
            string.IsNullOrWhiteSpace(appliedCouponCode) ? null : appliedCouponCode.Trim().ToUpperInvariant(),
            cart.ItemsSubtotal,
            authoritativeItemsDiscount,
            authoritativeDeliveryFee,
            authoritativeDeliveryDiscount,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            utcNow,
            actorUserId);

        foreach (var cartItem in cart.Items)
        {
            order._items.Add(new OrderItem(
                Guid.NewGuid(),
                cart.TenantId,
                orderId,
                cartItem.ItemKind,
                cartItem.ProductId,
                cartItem.CategoryId,
                cartItem.VariationId,
                cartItem.AcaiCupSizeId,
                cartItem.WeighingRecordId,
                cartItem.ItemTitle,
                cartItem.CustomizationSummaryText,
                cartItem.SelectionsPayloadJson,
                cartItem.UnitPrice,
                cartItem.Quantity,
                cartItem.LineSubtotal,
                cartItem.CustomerNotes));
        }

        return Result.Success(order);
    }

    public Result TransitionTo(OrderStatus nextStatus, Guid? actorUserId, string reason, DateTimeOffset utcNow)
    {
        if (nextStatus == OrderStatus.Cancelled)
            return Cancel(actorUserId, reason, utcNow);

        if (Status is OrderStatus.Completed or OrderStatus.Cancelled)
            return Result.Failure(Error.Conflict("Order.TerminalStatus", $"Pedido em estado final '{Status}' não aceita novas transições."));

        var allowed = (Status, nextStatus) switch
        {
            (OrderStatus.Received, OrderStatus.Accepted) => true,
            (OrderStatus.Received, OrderStatus.InPreparation) => true,
            (OrderStatus.Accepted, OrderStatus.InPreparation) => true,
            (OrderStatus.InPreparation, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.Dispatched) when FulfillmentMode == FulfillmentMode.Delivery => true,
            (OrderStatus.Ready, OrderStatus.Completed) => true,
            (OrderStatus.Dispatched, OrderStatus.Completed) => true,
            _ => false
        };

        if (!allowed)
        {
            return Result.Failure(Error.Validation(
                "Order.InvalidKanbanTransition",
                $"Transição inválida de '{Status}' para '{nextStatus}' na modalidade '{FulfillmentMode}'."));
        }

        var previous = Status;
        Status = nextStatus;
        if (nextStatus == OrderStatus.Completed)
            CompletedAtUtc = utcNow;

        _statusHistory.Add(new OrderStatusHistory(
            Guid.NewGuid(),
            TenantId,
            Id,
            previous,
            nextStatus,
            actorUserId,
            string.IsNullOrWhiteSpace(reason) ? $"Movido para {nextStatus}" : reason.Trim(),
            utcNow));

        return Result.Success();
    }

    public Result Cancel(Guid? actorUserId, string reason, DateTimeOffset utcNow)
    {
        if (Status == OrderStatus.Cancelled)
            return Result.Failure(Error.Conflict("Order.AlreadyCancelled", "Pedido já está cancelado."));
        if (Status == OrderStatus.Completed)
            return Result.Failure(Error.Conflict("Order.CannotCancelCompleted", "Pedido já entregue/concluído não pode ser cancelado por esta transição."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Order.CancellationReasonRequired", "Motivo do cancelamento é obrigatório."));

        var previous = Status;
        Status = OrderStatus.Cancelled;
        CancelledAtUtc = utcNow;
        CancellationReason = reason.Trim();

        _statusHistory.Add(new OrderStatusHistory(
            Guid.NewGuid(),
            TenantId,
            Id,
            previous,
            OrderStatus.Cancelled,
            actorUserId,
            CancellationReason,
            utcNow));

        return Result.Success();
    }
}
