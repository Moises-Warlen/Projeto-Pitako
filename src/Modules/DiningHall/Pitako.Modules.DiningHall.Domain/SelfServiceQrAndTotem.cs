using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DiningHall.Domain;

public enum QrSessionStatus
{
    Active = 1,
    BillRequested = 2,
    Closed = 3,
    Expired = 4
}

public enum WaiterCallType
{
    CallWaiter = 1,
    RequestBill = 2,
    NeedAssistance = 3,
    RequestCutlery = 4
}

public enum WaiterCallStatus
{
    Pending = 1,
    Acknowledged = 2,
    Completed = 3,
    Cancelled = 4
}

public enum TotemTerminalStatus
{
    Active = 1,
    Maintenance = 2,
    Inactive = 3
}

public enum TotemFulfillmentChoice
{
    DineIn = 1,
    CounterPickup = 2
}

public enum TotemPaymentChoice
{
    CardTapOnTotem = 1,
    PixOnTotem = 2,
    PayAtCashier = 3
}

public enum TotemSessionStatus
{
    Draft = 1,
    Confirmed = 2,
    Expired = 3,
    Cancelled = 4
}

/// <summary>
/// Sessão de autoatendimento vinculada à leitura de QR Code de uma mesa específica (Seção 25).
/// </summary>
public sealed class QrTableSession : TenantScopedAggregateRoot
{
    private QrTableSession()
    {
        SessionToken = string.Empty;
        TableCode = string.Empty;
        CustomerName = string.Empty;
    }

    private QrTableSession(
        Guid id,
        Guid tenantId,
        string sessionToken,
        Guid diningTableId,
        Guid diningTabId,
        string tableCode,
        string customerName,
        DateTimeOffset openedAtUtc,
        DateTimeOffset expiresAtUtc) : base(id, tenantId)
    {
        SessionToken = sessionToken;
        DiningTableId = diningTableId;
        DiningTabId = diningTabId;
        TableCode = tableCode;
        CustomerName = customerName;
        Status = QrSessionStatus.Active;
        OpenedAtUtc = openedAtUtc;
        LastActivityAtUtc = openedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string SessionToken { get; private set; }
    public Guid DiningTableId { get; private set; }
    public Guid DiningTabId { get; private set; }
    public string TableCode { get; private set; }
    public string CustomerName { get; private set; }
    public QrSessionStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset LastActivityAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static Result<QrTableSession> Create(
        Guid tenantId,
        DiningTable table,
        Guid diningTabId,
        string customerName,
        DateTimeOffset utcNow,
        int ttlMinutes = 180)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (tenantId == Guid.Empty)
            return Result.Failure<QrTableSession>(Error.Validation("QrSession.TenantRequired", "TenantId é obrigatório."));
        if (table.TenantId != tenantId)
            return Result.Failure<QrTableSession>(Error.Forbidden("QrSession.CrossTenantTable", "Mesa pertence a outro estabelecimento."));
        if (table.Status == DiningTableStatus.Inactive)
            return Result.Failure<QrTableSession>(Error.Conflict("Dining.TableInactive", "Mesa desativada não permite sessão QR."));
        if (diningTabId == Guid.Empty)
            return Result.Failure<QrTableSession>(Error.Validation("QrSession.TabRequired", "Comanda ativa é obrigatória."));
        if (ttlMinutes < 15)
            return Result.Failure<QrTableSession>(Error.Validation("QrSession.InvalidTtl", "Validade mínima da sessão QR é de 15 minutos."));

        var resolvedName = string.IsNullOrWhiteSpace(customerName)
            ? $"Cliente Mesa {table.TableCode}"
            : customerName.Trim();

        return Result.Success(new QrTableSession(
            Guid.NewGuid(),
            tenantId,
            $"QRS-{Guid.NewGuid():N}",
            table.Id,
            diningTabId,
            table.TableCode,
            resolvedName,
            utcNow,
            utcNow.AddMinutes(ttlMinutes)));
    }

    public bool IsExpired(DateTimeOffset utcNow) =>
        Status == QrSessionStatus.Expired || utcNow > ExpiresAtUtc;

    public Result EnsureOperational(DateTimeOffset utcNow)
    {
        if (Status == QrSessionStatus.Closed)
            return Result.Failure(Error.Conflict("QrSession.Closed", "Sessão QR Code encerrada."));
        if (IsExpired(utcNow))
        {
            Status = QrSessionStatus.Expired;
            return Result.Failure(Error.Conflict("QrSession.Expired", "Sessão QR Code expirada por inatividade."));
        }

        LastActivityAtUtc = utcNow;
        return Result.Success();
    }

    public Result RequestBill(DateTimeOffset utcNow)
    {
        var check = EnsureOperational(utcNow);
        if (check.IsFailure)
            return check;

        Status = QrSessionStatus.BillRequested;
        return Result.Success();
    }

    public void Close(DateTimeOffset utcNow)
    {
        LastActivityAtUtc = utcNow;
        Status = QrSessionStatus.Closed;
    }
}

/// <summary>
/// Chamado de atendimento do cliente na mesa via QR Code (chamar garçom, pedir conta, auxílio) (Seção 25).
/// </summary>
public sealed class WaiterCall : TenantScopedAggregateRoot
{
    private WaiterCall()
    {
        TableCode = string.Empty;
    }

    private WaiterCall(
        Guid id,
        Guid tenantId,
        Guid diningTableId,
        string tableCode,
        Guid? diningTabId,
        Guid? qrTableSessionId,
        WaiterCallType callType,
        string? customerNotes,
        DateTimeOffset requestedAtUtc) : base(id, tenantId)
    {
        DiningTableId = diningTableId;
        TableCode = tableCode;
        DiningTabId = diningTabId;
        QrTableSessionId = qrTableSessionId;
        CallType = callType;
        CustomerNotes = customerNotes;
        Status = WaiterCallStatus.Pending;
        RequestedAtUtc = requestedAtUtc;
    }

    public Guid DiningTableId { get; private set; }
    public string TableCode { get; private set; }
    public Guid? DiningTabId { get; private set; }
    public Guid? QrTableSessionId { get; private set; }
    public WaiterCallType CallType { get; private set; }
    public string? CustomerNotes { get; private set; }
    public WaiterCallStatus Status { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static Result<WaiterCall> Create(
        Guid tenantId,
        DiningTable table,
        WaiterCallType callType,
        Guid? diningTabId,
        Guid? qrTableSessionId,
        string? customerNotes,
        DateTimeOffset requestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (tenantId == Guid.Empty)
            return Result.Failure<WaiterCall>(Error.Validation("WaiterCall.TenantRequired", "TenantId é obrigatório."));
        if (table.TenantId != tenantId)
            return Result.Failure<WaiterCall>(Error.Forbidden("WaiterCall.CrossTenantTable", "Mesa pertence a outro estabelecimento."));

        return Result.Success(new WaiterCall(
            Guid.NewGuid(),
            tenantId,
            table.Id,
            table.TableCode,
            diningTabId,
            qrTableSessionId,
            callType,
            string.IsNullOrWhiteSpace(customerNotes) ? null : customerNotes.Trim(),
            requestedAtUtc));
    }

    public Result Acknowledge(Guid staffUserId, DateTimeOffset utcNow)
    {
        if (staffUserId == Guid.Empty)
            return Result.Failure(Error.Validation("WaiterCall.StaffRequired", "Atendente é obrigatório."));
        if (Status != WaiterCallStatus.Pending)
            return Result.Failure(Error.Conflict("WaiterCall.InvalidTransition", "Apenas chamados pendentes podem ser assumidos."));

        Status = WaiterCallStatus.Acknowledged;
        AcknowledgedByUserId = staffUserId;
        AcknowledgedAtUtc = utcNow;
        return Result.Success();
    }

    public Result Complete(Guid staffUserId, DateTimeOffset utcNow)
    {
        if (staffUserId == Guid.Empty)
            return Result.Failure(Error.Validation("WaiterCall.StaffRequired", "Atendente é obrigatório."));
        if (Status is not (WaiterCallStatus.Pending or WaiterCallStatus.Acknowledged))
            return Result.Failure(Error.Conflict("WaiterCall.AlreadyFinished", "Chamado já encerrado."));

        AcknowledgedByUserId ??= staffUserId;
        AcknowledgedAtUtc ??= utcNow;
        Status = WaiterCallStatus.Completed;
        CompletedAtUtc = utcNow;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset utcNow)
    {
        if (Status is WaiterCallStatus.Completed or WaiterCallStatus.Cancelled)
            return Result.Failure(Error.Conflict("WaiterCall.AlreadyFinished", "Chamado já encerrado."));

        Status = WaiterCallStatus.Cancelled;
        CompletedAtUtc = utcNow;
        return Result.Success();
    }
}

/// <summary>
/// Terminal físico de Totem de Autoatendimento cadastrado para o estabelecimento (Seção 25).
/// </summary>
public sealed class TotemTerminal : TenantScopedAggregateRoot
{
    private TotemTerminal()
    {
        TerminalCode = string.Empty;
        DisplayName = string.Empty;
    }

    private TotemTerminal(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string terminalCode,
        string displayName,
        TotemFulfillmentChoice defaultFulfillmentChoice,
        bool requireCustomerName,
        bool requireTableOrPagerNumber,
        int idleTimeoutSeconds) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        TerminalCode = terminalCode;
        DisplayName = displayName;
        DefaultFulfillmentChoice = defaultFulfillmentChoice;
        RequireCustomerName = requireCustomerName;
        RequireTableOrPagerNumber = requireTableOrPagerNumber;
        IdleTimeoutSeconds = idleTimeoutSeconds;
        Status = TotemTerminalStatus.Active;
    }

    public Guid? StoreUnitId { get; private set; }
    public string TerminalCode { get; private set; }
    public string DisplayName { get; private set; }
    public TotemTerminalStatus Status { get; private set; }
    public TotemFulfillmentChoice DefaultFulfillmentChoice { get; private set; }
    public bool RequireCustomerName { get; private set; }
    public bool RequireTableOrPagerNumber { get; private set; }
    public int IdleTimeoutSeconds { get; private set; }
    public DateTimeOffset? LastHeartbeatUtc { get; private set; }

    public static Result<TotemTerminal> Create(
        Guid tenantId,
        string terminalCode,
        string displayName,
        TotemFulfillmentChoice defaultFulfillmentChoice = TotemFulfillmentChoice.CounterPickup,
        bool requireCustomerName = true,
        bool requireTableOrPagerNumber = false,
        int idleTimeoutSeconds = 120,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TotemTerminal>(Error.Validation("Totem.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(terminalCode))
            return Result.Failure<TotemTerminal>(Error.Validation("Totem.CodeRequired", "Código do totem é obrigatório."));
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<TotemTerminal>(Error.Validation("Totem.NameRequired", "Nome de exibição do totem é obrigatório."));
        if (idleTimeoutSeconds < 15)
            return Result.Failure<TotemTerminal>(Error.Validation("Totem.InvalidTimeout", "Timeout de inatividade mínimo do totem é 15 segundos."));

        return Result.Success(new TotemTerminal(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            terminalCode.Trim().ToUpperInvariant(),
            displayName.Trim(),
            defaultFulfillmentChoice,
            requireCustomerName,
            requireTableOrPagerNumber,
            idleTimeoutSeconds));
    }

    public void SetStatus(TotemTerminalStatus status) => Status = status;

    public void RecordHeartbeat(DateTimeOffset utcNow) => LastHeartbeatUtc = utcNow;
}

/// <summary>
/// Linha de item em uma jornada de pedido no Totem de Autoatendimento.
/// </summary>
public sealed class TotemOrderLine : Entity<Guid>, ITenantScopedEntity
{
    private TotemOrderLine()
    {
        ItemTitle = string.Empty;
        CustomizationSummary = string.Empty;
        CanonicalSelectionsJson = "{}";
        UnitPrice = Money.Zero();
        LineSubtotal = Money.Zero();
    }

    internal TotemOrderLine(
        Guid id,
        Guid tenantId,
        Guid totemOrderSessionId,
        Guid? productId,
        Guid? acaiCupSizeId,
        string itemTitle,
        string customizationSummary,
        string canonicalSelectionsJson,
        Money unitPrice,
        int quantity,
        string? customerNotes) : base(id)
    {
        TenantId = tenantId;
        TotemOrderSessionId = totemOrderSessionId;
        ProductId = productId;
        AcaiCupSizeId = acaiCupSizeId;
        ItemTitle = itemTitle;
        CustomizationSummary = customizationSummary;
        CanonicalSelectionsJson = canonicalSelectionsJson;
        UnitPrice = Money.Create(unitPrice.Amount, unitPrice.Currency).Value;
        Quantity = quantity;
        LineSubtotal = UnitPrice.Multiply(quantity);
        CustomerNotes = customerNotes;
    }

    public Guid TenantId { get; private set; }
    public Guid TotemOrderSessionId { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? AcaiCupSizeId { get; private set; }
    public string ItemTitle { get; private set; }
    public string CustomizationSummary { get; private set; }
    public string CanonicalSelectionsJson { get; private set; }
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineSubtotal { get; private set; }
    public string? CustomerNotes { get; private set; }

    internal void IncreaseQuantity(int additionalQty)
    {
        Quantity += additionalQty;
        LineSubtotal = UnitPrice.Multiply(Quantity);
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em TotemOrderLine.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Sessão de compra e checkout idempotente em Totem de Autoatendimento com emissão de senha (Seção 25).
/// </summary>
public sealed class TotemOrderSession : TenantScopedAggregateRoot
{
    private readonly List<TotemOrderLine> _lines = new();

    private TotemOrderSession()
    {
        SessionToken = string.Empty;
        ItemsSubtotal = Money.Zero();
    }

    private TotemOrderSession(
        Guid id,
        Guid tenantId,
        Guid totemTerminalId,
        string sessionToken,
        TotemFulfillmentChoice fulfillmentChoice,
        string? customerName,
        string? pagerOrTableTag,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc) : base(id, tenantId)
    {
        TotemTerminalId = totemTerminalId;
        SessionToken = sessionToken;
        FulfillmentChoice = fulfillmentChoice;
        CustomerName = customerName;
        PagerOrTableTag = pagerOrTableTag;
        Status = TotemSessionStatus.Draft;
        ItemsSubtotal = Money.Zero();
        StartedAtUtc = startedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid TotemTerminalId { get; private set; }
    public string SessionToken { get; private set; }
    public TotemFulfillmentChoice FulfillmentChoice { get; private set; }
    public string? CustomerName { get; private set; }
    public string? PagerOrTableTag { get; private set; }
    public TotemSessionStatus Status { get; private set; }
    public TotemPaymentChoice? PaymentChoice { get; private set; }
    public string? PickupCallCode { get; private set; }
    public string? DispatchedOrderNumber { get; private set; }
    public Money ItemsSubtotal { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public IReadOnlyCollection<TotemOrderLine> Lines => _lines.AsReadOnly();

    public static Result<TotemOrderSession> Start(
        Guid tenantId,
        TotemTerminal terminal,
        TotemFulfillmentChoice? choice,
        string? customerName,
        string? pagerOrTableTag,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (tenantId == Guid.Empty)
            return Result.Failure<TotemOrderSession>(Error.Validation("Totem.TenantRequired", "TenantId é obrigatório."));
        if (terminal.TenantId != tenantId)
            return Result.Failure<TotemOrderSession>(Error.Forbidden("Totem.CrossTenantTerminal", "Totem pertence a outro estabelecimento."));
        if (terminal.Status != TotemTerminalStatus.Active)
            return Result.Failure<TotemOrderSession>(Error.Conflict("Totem.TerminalUnavailable", $"Totem '{terminal.TerminalCode}' está em status {terminal.Status}."));
        if (terminal.RequireCustomerName && string.IsNullOrWhiteSpace(customerName))
            return Result.Failure<TotemOrderSession>(Error.Validation("Totem.CustomerNameRequired", "Este totem exige identificação do nome do cliente."));
        if (terminal.RequireTableOrPagerNumber && string.IsNullOrWhiteSpace(pagerOrTableTag))
            return Result.Failure<TotemOrderSession>(Error.Validation("Totem.PagerOrTableRequired", "Este totem exige número da mesa ou pager."));

        return Result.Success(new TotemOrderSession(
            Guid.NewGuid(),
            tenantId,
            terminal.Id,
            $"TTM-{Guid.NewGuid():N}",
            choice ?? terminal.DefaultFulfillmentChoice,
            string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim(),
            string.IsNullOrWhiteSpace(pagerOrTableTag) ? null : pagerOrTableTag.Trim(),
            utcNow,
            utcNow.AddSeconds(terminal.IdleTimeoutSeconds)));
    }

    public Result AddLine(
        Guid? productId,
        Guid? acaiCupSizeId,
        string itemTitle,
        string customizationSummary,
        string canonicalSelectionsJson,
        Money unitPrice,
        int quantity,
        string? customerNotes,
        int extendIdleSeconds,
        DateTimeOffset utcNow)
    {
        var check = EnsureEditable(utcNow, extendIdleSeconds);
        if (check.IsFailure)
            return check;

        if (string.IsNullOrWhiteSpace(itemTitle))
            return Result.Failure(Error.Validation("Totem.ItemTitleRequired", "Descrição do item é obrigatória."));
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Totem.InvalidQty", "Quantidade deve ser positiva."));
        ArgumentNullException.ThrowIfNull(unitPrice);

        var normalizedJson = string.IsNullOrWhiteSpace(canonicalSelectionsJson) ? "{}" : canonicalSelectionsJson.Trim();
        var normalizedNotes = string.IsNullOrWhiteSpace(customerNotes) ? null : customerNotes.Trim();

        var matching = _lines.FirstOrDefault(l =>
            l.ProductId == (productId == Guid.Empty ? null : productId) &&
            l.AcaiCupSizeId == (acaiCupSizeId == Guid.Empty ? null : acaiCupSizeId) &&
            string.Equals(l.CanonicalSelectionsJson, normalizedJson, StringComparison.Ordinal) &&
            string.Equals(l.CustomerNotes, normalizedNotes, StringComparison.Ordinal));

        if (matching is not null)
        {
            matching.IncreaseQuantity(quantity);
        }
        else
        {
            _lines.Add(new TotemOrderLine(
                Guid.NewGuid(),
                TenantId,
                Id,
                productId == Guid.Empty ? null : productId,
                acaiCupSizeId == Guid.Empty ? null : acaiCupSizeId,
                itemTitle.Trim(),
                string.IsNullOrWhiteSpace(customizationSummary) ? "Padrão" : customizationSummary.Trim(),
                normalizedJson,
                unitPrice,
                quantity,
                normalizedNotes));
        }

        RecalculateSubtotal();
        return Result.Success();
    }

    public Result RemoveLine(Guid lineId, int extendIdleSeconds, DateTimeOffset utcNow)
    {
        var check = EnsureEditable(utcNow, extendIdleSeconds);
        if (check.IsFailure)
            return check;

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(Error.NotFound("Totem.LineNotFound", "Item não encontrado no carrinho do totem."));

        _lines.Remove(line);
        RecalculateSubtotal();
        return Result.Success();
    }

    public Result Confirm(
        TotemPaymentChoice paymentChoice,
        string pickupCallCode,
        string? dispatchedOrderNumber,
        string? idempotencyKey,
        DateTimeOffset utcNow)
    {
        if (Status == TotemSessionStatus.Confirmed)
            return Result.Success();
        if (Status == TotemSessionStatus.Cancelled)
            return Result.Failure(Error.Conflict("Totem.SessionCancelled", "Sessão cancelada."));
        if (Status == TotemSessionStatus.Expired || utcNow > ExpiresAtUtc)
        {
            Status = TotemSessionStatus.Expired;
            return Result.Failure(Error.Conflict("Totem.SessionExpired", "Sessão expirada por inatividade no totem."));
        }
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("Totem.EmptyBasket", "Adicione ao menos um item antes de confirmar no totem."));
        if (string.IsNullOrWhiteSpace(pickupCallCode))
            return Result.Failure(Error.Validation("Totem.PickupCodeRequired", "Senha de chamada do pedido é obrigatória."));

        PaymentChoice = paymentChoice;
        PickupCallCode = pickupCallCode.Trim().ToUpperInvariant();
        DispatchedOrderNumber = string.IsNullOrWhiteSpace(dispatchedOrderNumber) ? null : dispatchedOrderNumber.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        Status = TotemSessionStatus.Confirmed;
        ConfirmedAtUtc = utcNow;
        return Result.Success();
    }

    public void Cancel() => Status = TotemSessionStatus.Cancelled;

    private Result EnsureEditable(DateTimeOffset utcNow, int extendIdleSeconds)
    {
        if (Status == TotemSessionStatus.Confirmed)
            return Result.Failure(Error.Conflict("Totem.AlreadyConfirmed", "Pedido já confirmado no totem."));
        if (Status == TotemSessionStatus.Cancelled)
            return Result.Failure(Error.Conflict("Totem.SessionCancelled", "Sessão cancelada."));
        if (Status == TotemSessionStatus.Expired || utcNow > ExpiresAtUtc)
        {
            Status = TotemSessionStatus.Expired;
            return Result.Failure(Error.Conflict("Totem.SessionExpired", "Sessão expirada por inatividade no totem."));
        }

        if (extendIdleSeconds > 0)
            ExpiresAtUtc = utcNow.AddSeconds(extendIdleSeconds);

        return Result.Success();
    }

    private void RecalculateSubtotal()
    {
        var currency = ItemsSubtotal.Currency;
        var sum = Money.Zero(currency);
        foreach (var line in _lines)
            sum = sum.Add(line.LineSubtotal);

        ItemsSubtotal = sum;
    }
}
