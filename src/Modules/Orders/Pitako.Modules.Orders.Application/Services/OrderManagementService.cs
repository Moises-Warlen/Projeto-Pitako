using System.Text.Json;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Orders.Application.Services;

public sealed record CheckoutCartRequest(
    string CartSessionKey,
    OrderChannel Channel,
    FulfillmentMode FulfillmentMode,
    string CustomerName,
    string? CustomerPhone = null,
    Guid? StoreUnitId = null,
    string? DeliveryAddressSummary = null,
    double? DestinationLatitude = null,
    double? DestinationLongitude = null,
    string? Neighborhood = null,
    string? ZipCode = null,
    decimal? PrecomputedDistanceKm = null,
    string? IdempotencyKey = null,
    string? PaymentMethod = null,
    bool LgpdConsentAccepted = true,
    bool AllowFallbackZeroFreightIfNoPolicy = false);

public sealed record KanbanBoardView(
    IReadOnlyList<Order> Received,
    IReadOnlyList<Order> Accepted,
    IReadOnlyList<Order> InPreparation,
    IReadOnlyList<Order> Ready,
    IReadOnlyList<Order> Dispatched);

/// <summary>
/// Serviço central de Checkout, Central de Pedidos, máquina de estados Kanban em tempo real
/// e orquestração entre Delivery, Cupons, Balança e Baixa de Estoque (Seções 16, 17, 18, 20 e 21).
/// </summary>
public sealed class OrderManagementService
{
    private readonly IShoppingCartRepository _cartRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly IDeliveryFreightQuoteService _deliveryQuote;
    private readonly ICouponValidationService _couponService;
    private readonly IInventoryOrderDeductionService _inventoryDeduction;
    private readonly IWeighingOrderAttachmentService _weighingAttachment;
    private readonly IOrderRealtimeNotifier _realtimeNotifier;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public OrderManagementService(
        IShoppingCartRepository cartRepo,
        IOrderRepository orderRepo,
        IDeliveryFreightQuoteService deliveryQuote,
        ICouponValidationService couponService,
        IInventoryOrderDeductionService inventoryDeduction,
        IWeighingOrderAttachmentService weighingAttachment,
        IOrderRealtimeNotifier realtimeNotifier,
        ITenantContext tenantContext,
        IClock clock)
    {
        _cartRepo = cartRepo;
        _orderRepo = orderRepo;
        _deliveryQuote = deliveryQuote;
        _couponService = couponService;
        _inventoryDeduction = inventoryDeduction;
        _weighingAttachment = weighingAttachment;
        _realtimeNotifier = realtimeNotifier;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<Order>> CheckoutCartAsync(
        User actor,
        CheckoutCartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<Order>(guard.Error);

        var normalizedKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existingOrder = await _orderRepo.FindByIdempotencyKeyAsync(normalizedKey, cancellationToken);
            if (existingOrder is not null)
            {
                TenantGuard.EnsureOwnership(_tenantContext, existingOrder);
                return Result.Success(existingOrder);
            }
        }

        var cart = await _cartRepo.FindBySessionKeyAsync(request.CartSessionKey.Trim(), cancellationToken);
        if (cart is null)
            return Result.Failure<Order>(Error.NotFound("Cart.NotFound", "Carrinho não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, cart);
        if (cart.Items.Count == 0)
            return Result.Failure<Order>(Error.Validation("Order.EmptyCart", "Carrinho vazio."));

        var currency = cart.ItemsSubtotal.Currency;
        var baseDeliveryFee = Money.Zero(currency);
        var autoDeliveryDiscount = Money.Zero(currency);

        if (request.FulfillmentMode == FulfillmentMode.Delivery)
        {
            var freightRes = await _deliveryQuote.QuoteFreightAsync(
                request.StoreUnitId,
                new DeliveryFreightQuoteRequest(
                    cart.ItemsSubtotal,
                    request.DestinationLatitude,
                    request.DestinationLongitude,
                    request.Neighborhood,
                    request.ZipCode,
                    request.PrecomputedDistanceKm),
                cancellationToken);

            if (freightRes.IsFailure)
            {
                if (!request.AllowFallbackZeroFreightIfNoPolicy || freightRes.Error.Code != "Delivery.PolicyNotConfigured")
                    return Result.Failure<Order>(freightRes.Error);
            }
            else
            {
                baseDeliveryFee = freightRes.Value.BaseDeliveryFee;
                autoDeliveryDiscount = freightRes.Value.IsFreeShippingApplied
                    ? freightRes.Value.BaseDeliveryFee
                    : Money.Zero(currency);
            }
        }

        var netDeliveryBeforeCoupon = Money.Create(
            Math.Max(0m, baseDeliveryFee.Amount - autoDeliveryDiscount.Amount),
            currency).Value;

        var itemsDiscount = Money.Zero(currency);
        var couponDeliveryDiscount = Money.Zero(currency);
        var appliedCoupon = cart.AppliedCouponCode;

        var basketLines = cart.Items
            .Select(i => new CouponBasketLine(i.ProductId, i.CategoryId, i.LineSubtotal, i.HasNonCombinablePromotion))
            .ToList();

        if (!string.IsNullOrWhiteSpace(appliedCoupon))
        {
            var evalRes = await _couponService.EvaluateCouponAsync(
                appliedCoupon,
                cart.CustomerIdentifier,
                basketLines,
                netDeliveryBeforeCoupon,
                cancellationToken);

            if (evalRes.IsFailure)
                return Result.Failure<Order>(evalRes.Error);

            itemsDiscount = evalRes.Value.ItemsDiscount;
            couponDeliveryDiscount = evalRes.Value.DeliveryDiscount;
        }

        var totalDeliveryDiscount = autoDeliveryDiscount.Add(couponDeliveryDiscount);
        var sequence = await _orderRepo.CountAllAsync(cancellationToken) + 1;
        var orderNumber = $"#{sequence:D4}";
        var utcNow = _clock.UtcNow;

        var orderRes = Order.CreateFromCart(
            cart,
            orderNumber,
            request.Channel,
            request.FulfillmentMode,
            request.CustomerName,
            request.CustomerPhone,
            request.DeliveryAddressSummary,
            baseDeliveryFee,
            totalDeliveryDiscount,
            itemsDiscount,
            appliedCoupon,
            request.StoreUnitId,
            actor.Id,
            normalizedKey,
            utcNow,
            request.PaymentMethod,
            request.LgpdConsentAccepted);

        if (orderRes.IsFailure)
            return orderRes;

        var order = orderRes.Value;

        // Baixa automática de estoque configurável por ficha técnica + ingredientes personalizados (sem duplicidade)
        var stockLines = BuildOrderStockLines(order.Items);

        if (stockLines.Count > 0)
        {
            var stockRes = await _inventoryDeduction.DeductForOrderSaleAsync(
                actor,
                order.Id,
                order.OrderNumber,
                stockLines,
                cancellationToken);

            if (stockRes.IsFailure)
                return Result.Failure<Order>(stockRes.Error);
        }

        // Vincula registros de pesagem ao pedido definitivo
        foreach (var weighedItem in order.Items.Where(i => i.WeighingRecordId.HasValue))
        {
            var attachRes = await _weighingAttachment.AttachToOrderAsync(
                actor,
                weighedItem.WeighingRecordId!.Value,
                order.Id,
                cancellationToken);

            if (attachRes.IsFailure)
                return Result.Failure<Order>(attachRes.Error);
        }

        // Registra resgate definitivo do cupom se houver
        if (!string.IsNullOrWhiteSpace(appliedCoupon))
        {
            var redeemRes = await _couponService.RedeemCouponForOrderAsync(
                appliedCoupon,
                order.Id,
                cart.CustomerIdentifier,
                basketLines,
                netDeliveryBeforeCoupon,
                cancellationToken);

            if (redeemRes.IsFailure)
                return Result.Failure<Order>(redeemRes.Error);
        }

        await _orderRepo.AddAsync(order, cancellationToken);
        _cartRepo.Remove(cart);
        await _orderRepo.SaveChangesAsync(cancellationToken);

        await _realtimeNotifier.NotifyAsync(new OrderRealtimeEvent(
            order.TenantId,
            order.Id,
            order.OrderNumber,
            "OrderCreated",
            order.Status,
            utcNow), cancellationToken);

        return Result.Success(order);
    }

    public async Task<Result<Order>> TransitionKanbanStatusAsync(
        User actor,
        Guid orderId,
        OrderStatus nextStatus,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var requiredAction = nextStatus == OrderStatus.Cancelled
            ? PermissionAction.Cancel
            : PermissionAction.Edit;

        var guard = ValidateTenantActor(actor, requiredAction);
        if (guard.IsFailure)
            return Result.Failure<Order>(guard.Error);

        var order = await _orderRepo.FindByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Result.Failure<Order>(Error.NotFound("Order.NotFound", "Pedido não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, order);

        var utcNow = _clock.UtcNow;
        var transitionRes = order.TransitionTo(nextStatus, actor.Id, reason, utcNow);
        if (transitionRes.IsFailure)
            return Result.Failure<Order>(transitionRes.Error);

        if (nextStatus == OrderStatus.Cancelled)
        {
            var stockReversal = await _inventoryDeduction.ReverseOrderDeductionAsync(actor, order.Id, reason, cancellationToken);
            if (stockReversal.IsFailure)
                return Result.Failure<Order>(stockReversal.Error);

            if (!string.IsNullOrWhiteSpace(order.AppliedCouponCode))
            {
                var couponReversal = await _couponService.ReverseCouponRedemptionAsync(order.AppliedCouponCode, order.Id, cancellationToken);
                if (couponReversal.IsFailure)
                    return Result.Failure<Order>(couponReversal.Error);
            }
        }

        await _orderRepo.SaveChangesAsync(cancellationToken);

        await _realtimeNotifier.NotifyAsync(new OrderRealtimeEvent(
            order.TenantId,
            order.Id,
            order.OrderNumber,
            nextStatus == OrderStatus.Cancelled ? "OrderCancelled" : "OrderStatusChanged",
            order.Status,
            utcNow), cancellationToken);

        return Result.Success(order);
    }

    public async Task<Result<Order>> ConfirmPaymentAsync(
        User actor,
        Guid orderId,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<Order>(guard.Error);

        var order = await _orderRepo.FindByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Result.Failure<Order>(Error.NotFound("Order.NotFound", "Pedido não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, order);
        var res = order.ConfirmPayment(actor.Id, notes, _clock.UtcNow);
        if (res.IsFailure)
            return Result.Failure<Order>(res.Error);

        await _orderRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(order);
    }

    public async Task<Result<KanbanBoardView>> GetKanbanBoardAsync(User actor, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<KanbanBoardView>(guard.Error);

        var active = await _orderRepo.ListKanbanActiveAsync(cancellationToken);
        return Result.Success(new KanbanBoardView(
            active.Where(o => o.Status == OrderStatus.Received).ToList(),
            active.Where(o => o.Status == OrderStatus.Accepted).ToList(),
            active.Where(o => o.Status == OrderStatus.InPreparation).ToList(),
            active.Where(o => o.Status == OrderStatus.Ready).ToList(),
            active.Where(o => o.Status == OrderStatus.Dispatched).ToList()));
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar pedidos de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Orders, action))
            return Result.Failure<Guid>(Error.Forbidden("Orders.Forbidden", "Permissão insuficiente para operar pedidos."));

        return Result.Success(currentTenantId);
    }

    private static List<OrderStockLineInput> BuildOrderStockLines(IEnumerable<OrderItem> items)
    {
        var lines = new List<OrderStockLineInput>();
        foreach (var item in items)
        {
            var customIngredients = new List<OrderCustomIngredientInput>();
            var expandedCombo = false;

            if (!string.IsNullOrWhiteSpace(item.SelectionsPayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(item.SelectionsPayloadJson);
                    if (doc.RootElement.TryGetProperty("customIngredients", out var customEl) && customEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ci in customEl.EnumerateArray())
                        {
                            if (ci.TryGetProperty("SupplyItemId", out var sidEl) || ci.TryGetProperty("supplyItemId", out sidEl))
                            {
                                if (Guid.TryParse(sidEl.GetString(), out var supplyId) && supplyId != Guid.Empty)
                                {
                                    var qty = ci.TryGetProperty("GrossQuantity", out var qEl) ? qEl.GetDecimal()
                                        : ci.TryGetProperty("grossQuantity", out var qEl2) ? qEl2.GetDecimal() : 0m;
                                    var unit = ci.TryGetProperty("UnitCode", out var uEl) ? (uEl.GetString() ?? "unidade")
                                        : ci.TryGetProperty("unitCode", out var uEl2) ? (uEl2.GetString() ?? "unidade") : "unidade";
                                    var name = ci.TryGetProperty("IngredientName", out var nEl) ? (nEl.GetString() ?? "Adicional")
                                        : ci.TryGetProperty("ingredientName", out var nEl2) ? (nEl2.GetString() ?? "Adicional") : "Adicional";
                                    if (qty > 0m)
                                        customIngredients.Add(new OrderCustomIngredientInput(supplyId, qty, unit, name));
                                }
                            }
                        }
                    }

                    if (item.ItemKind == CartItemKind.Combo &&
                        doc.RootElement.TryGetProperty("items", out var comboItemsEl) &&
                        comboItemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ci in comboItemsEl.EnumerateArray())
                        {
                            if (ci.TryGetProperty("productId", out var pidEl) && Guid.TryParse(pidEl.GetString(), out var pid))
                            {
                                var cQty = ci.TryGetProperty("qty", out var qEl) ? qEl.GetInt32() : 1;
                                lines.Add(new OrderStockLineInput(pid, item.Quantity * Math.Max(1, cQty), "unidade"));
                                expandedCombo = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Payload JSON legado/simples sem customIngredients
                }
            }

            if (!expandedCombo && (item.ProductId.HasValue || customIngredients.Count > 0))
            {
                lines.Add(new OrderStockLineInput(
                    item.ProductId ?? Guid.Empty,
                    item.Quantity,
                    "unidade",
                    customIngredients.Count > 0 ? customIngredients : null));
            }
        }
        return lines;
    }
}
