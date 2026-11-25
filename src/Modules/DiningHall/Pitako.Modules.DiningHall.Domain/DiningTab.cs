using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DiningHall.Domain;

public enum DiningTabOrigin
{
    Waiter = 1,
    QrCodeTable = 2
}

public enum DiningTabStatus
{
    Open = 1,
    ClosingRequested = 2,
    Closed = 3,
    Cancelled = 4
}

public enum DiningTabItemStatus
{
    Ordered = 1,
    Served = 2,
    Cancelled = 3
}

public sealed record TabSplitShare(int PayerIndex, Money Amount);

/// <summary>
/// Item lançado na comanda/mesa por garçom, atendimento QR ou balcão (Seção 24).
/// </summary>
public sealed class DiningTabItem : Entity<Guid>, ITenantScopedEntity
{
    private DiningTabItem()
    {
        ItemTitle = string.Empty;
        CustomizationSummary = string.Empty;
        UnitPrice = Money.Zero();
        LineSubtotal = Money.Zero();
    }

    internal DiningTabItem(
        Guid id,
        Guid tenantId,
        Guid diningTabId,
        Guid? productId,
        string itemTitle,
        string customizationSummary,
        Money unitPrice,
        int quantity,
        Guid? addedByUserId,
        DateTimeOffset addedAtUtc) : base(id)
    {
        TenantId = tenantId;
        DiningTabId = diningTabId;
        ProductId = productId;
        ItemTitle = itemTitle;
        CustomizationSummary = customizationSummary;
        UnitPrice = Money.Create(unitPrice.Amount, unitPrice.Currency).Value;
        Quantity = quantity;
        LineSubtotal = UnitPrice.Multiply(quantity);
        Status = DiningTabItemStatus.Ordered;
        AddedByUserId = addedByUserId;
        AddedAtUtc = addedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid DiningTabId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ItemTitle { get; private set; }
    public string CustomizationSummary { get; private set; }
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineSubtotal { get; private set; }
    public DiningTabItemStatus Status { get; private set; }
    public Guid? AddedByUserId { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    internal void MarkServed() => Status = DiningTabItemStatus.Served;
    internal void Cancel(string reason)
    {
        Status = DiningTabItemStatus.Cancelled;
        CancellationReason = reason.Trim();
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em DiningTabItem.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Registro de auditoria da comanda (transferência de mesa, troca de garçom, taxa de serviço, divisão/fechamento).
/// </summary>
public sealed class DiningTabAuditEvent : Entity<Guid>, ITenantScopedEntity
{
    private DiningTabAuditEvent()
    {
        Action = string.Empty;
        Details = string.Empty;
    }

    internal DiningTabAuditEvent(
        Guid id,
        Guid tenantId,
        Guid diningTabId,
        string action,
        string details,
        Guid? actorUserId,
        DateTimeOffset occurredAtUtc) : base(id)
    {
        TenantId = tenantId;
        DiningTabId = diningTabId;
        Action = action;
        Details = details;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid DiningTabId { get; private set; }
    public string Action { get; private set; }
    public string Details { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em DiningTabAuditEvent.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Comanda de consumo no salão com controle por garçom, transferência entre mesas,
/// taxa de serviço configurável, divisão igualitária/parcial e fechamento (Seção 24).
/// </summary>
public sealed class DiningTab : TenantScopedAggregateRoot
{
    private readonly List<DiningTabItem> _items = new();
    private readonly List<DiningTabAuditEvent> _auditEvents = new();

    private DiningTab()
    {
        TabCode = string.Empty;
        CustomerName = string.Empty;
        ItemsSubtotal = Money.Zero();
        ServiceChargeAmount = Money.Zero();
        TotalAmount = Money.Zero();
        PaidAmount = Money.Zero();
    }

    private DiningTab(
        Guid id,
        Guid tenantId,
        string tabCode,
        Guid? diningTableId,
        Guid waiterUserId,
        string customerName,
        int guestCount,
        decimal serviceChargePercent,
        DiningTabOrigin origin,
        DateTimeOffset openedAtUtc) : base(id, tenantId)
    {
        TabCode = tabCode;
        DiningTableId = diningTableId;
        WaiterUserId = waiterUserId;
        CustomerName = customerName;
        GuestCount = guestCount;
        ServiceChargePercent = decimal.Round(serviceChargePercent, 2, MidpointRounding.ToEven);
        Origin = origin;
        Status = DiningTabStatus.Open;
        OpenedAtUtc = openedAtUtc;
        ItemsSubtotal = Money.Zero();
        ServiceChargeAmount = Money.Zero();
        TotalAmount = Money.Zero();
        PaidAmount = Money.Zero();

        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(),
            tenantId,
            id,
            "TabOpened",
            $"Comanda '{tabCode}' aberta ({origin}) para {guestCount} pessoa(s).",
            waiterUserId == Guid.Empty ? null : waiterUserId,
            openedAtUtc));
    }

    public string TabCode { get; private set; }
    public Guid? DiningTableId { get; private set; }
    public Guid WaiterUserId { get; private set; }
    public string CustomerName { get; private set; }
    public int GuestCount { get; private set; }
    public decimal ServiceChargePercent { get; private set; }
    public DiningTabOrigin Origin { get; private set; }
    public DiningTabStatus Status { get; private set; }
    public Money ItemsSubtotal { get; private set; }
    public Money ServiceChargeAmount { get; private set; }
    public Money TotalAmount { get; private set; }
    public Money PaidAmount { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public Money RemainingBalance =>
        Money.Create(Math.Max(0m, TotalAmount.Amount - PaidAmount.Amount), TotalAmount.Currency).Value;

    public IReadOnlyCollection<DiningTabItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<DiningTabAuditEvent> AuditEvents => _auditEvents.AsReadOnly();

    public static Result<DiningTab> Open(
        Guid tenantId,
        string tabCode,
        Guid waiterUserId,
        string customerName,
        int guestCount = 1,
        decimal serviceChargePercent = 10m,
        DiningTable? table = null,
        DateTimeOffset? openedAtUtc = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DiningTab>(Error.Validation("Tab.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(tabCode))
            return Result.Failure<DiningTab>(Error.Validation("Tab.CodeRequired", "Código da comanda é obrigatório."));
        if (waiterUserId == Guid.Empty)
            return Result.Failure<DiningTab>(Error.Validation("Tab.WaiterRequired", "Garçom/responsável é obrigatório."));
        if (string.IsNullOrWhiteSpace(customerName))
            return Result.Failure<DiningTab>(Error.Validation("Tab.CustomerRequired", "Identificação do cliente na comanda é obrigatória."));
        if (guestCount <= 0)
            return Result.Failure<DiningTab>(Error.Validation("Tab.InvalidGuests", "Número de pessoas deve ser positivo."));
        if (serviceChargePercent is < 0m or > 30m)
            return Result.Failure<DiningTab>(Error.Validation("Tab.InvalidServiceCharge", "Taxa de serviço deve estar entre 0% e 30%."));

        var tab = new DiningTab(
            Guid.NewGuid(),
            tenantId,
            tabCode.Trim().ToUpperInvariant(),
            table?.Id,
            waiterUserId,
            customerName.Trim(),
            guestCount,
            serviceChargePercent,
            DiningTabOrigin.Waiter,
            openedAtUtc ?? DateTimeOffset.UtcNow);

        if (table is not null)
        {
            if (table.TenantId != tenantId)
                return Result.Failure<DiningTab>(Error.Forbidden("Tab.CrossTenantTable", "Mesa pertence a outro estabelecimento."));

            var occupyRes = table.OccupyWithTab(tab.Id);
            if (occupyRes.IsFailure)
                return Result.Failure<DiningTab>(occupyRes.Error);
        }

        return Result.Success(tab);
    }

    /// <summary>
    /// Abre comanda de autoatendimento diretamente pelo QR Code da mesa sem exigir garçom inicial.
    /// </summary>
    public static Result<DiningTab> OpenSelfService(
        Guid tenantId,
        string tabCode,
        DiningTable table,
        string customerName,
        int guestCount = 1,
        decimal serviceChargePercent = 10m,
        DateTimeOffset? openedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (tenantId == Guid.Empty)
            return Result.Failure<DiningTab>(Error.Validation("Tab.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(tabCode))
            return Result.Failure<DiningTab>(Error.Validation("Tab.CodeRequired", "Código da comanda é obrigatório."));
        if (string.IsNullOrWhiteSpace(customerName))
            return Result.Failure<DiningTab>(Error.Validation("Tab.CustomerRequired", "Identificação do cliente na comanda é obrigatória."));
        if (guestCount <= 0)
            return Result.Failure<DiningTab>(Error.Validation("Tab.InvalidGuests", "Número de pessoas deve ser positivo."));
        if (serviceChargePercent is < 0m or > 30m)
            return Result.Failure<DiningTab>(Error.Validation("Tab.InvalidServiceCharge", "Taxa de serviço deve estar entre 0% e 30%."));
        if (table.TenantId != tenantId)
            return Result.Failure<DiningTab>(Error.Forbidden("Tab.CrossTenantTable", "Mesa pertence a outro estabelecimento."));

        var tab = new DiningTab(
            Guid.NewGuid(),
            tenantId,
            tabCode.Trim().ToUpperInvariant(),
            table.Id,
            Guid.Empty,
            customerName.Trim(),
            guestCount,
            serviceChargePercent,
            DiningTabOrigin.QrCodeTable,
            openedAtUtc ?? DateTimeOffset.UtcNow);

        var occupyRes = table.OccupyWithTab(tab.Id);
        if (occupyRes.IsFailure)
            return Result.Failure<DiningTab>(occupyRes.Error);

        return Result.Success(tab);
    }

    public Result RequestClosing(Guid? actorUserId, DiningTable? linkedTable, DateTimeOffset utcNow)
    {
        if (Status is DiningTabStatus.Closed or DiningTabStatus.Cancelled)
            return Result.Failure(Error.Conflict("Tab.AlreadyClosed", "Comanda já encerrada."));

        Status = DiningTabStatus.ClosingRequested;
        linkedTable?.MarkAwaitingPayment();
        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(),
            TenantId,
            Id,
            "ClosingRequested",
            "Solicitação de fechamento de conta registrada.",
            actorUserId == Guid.Empty ? null : actorUserId,
            utcNow));
        return Result.Success();
    }

    public Result<DiningTabItem> AddItem(
        Guid? productId,
        string itemTitle,
        string customizationSummary,
        Money unitPrice,
        int quantity,
        Guid actorUserId,
        DateTimeOffset utcNow)
    {
        if (Status is DiningTabStatus.Closed or DiningTabStatus.Cancelled)
            return Result.Failure<DiningTabItem>(Error.Conflict("Tab.NotOpen", "Comanda já encerrada não aceita novos lançamentos."));
        if (string.IsNullOrWhiteSpace(itemTitle))
            return Result.Failure<DiningTabItem>(Error.Validation("Tab.ItemTitleRequired", "Descrição do item é obrigatória."));
        if (quantity <= 0)
            return Result.Failure<DiningTabItem>(Error.Validation("Tab.InvalidQty", "Quantidade do item deve ser positiva."));
        ArgumentNullException.ThrowIfNull(unitPrice);

        var item = new DiningTabItem(
            Guid.NewGuid(),
            TenantId,
            Id,
            productId == Guid.Empty ? null : productId,
            itemTitle.Trim(),
            string.IsNullOrWhiteSpace(customizationSummary) ? "Padrão" : customizationSummary.Trim(),
            unitPrice,
            quantity,
            actorUserId,
            utcNow);

        _items.Add(item);
        RecalculateTotals();
        return Result.Success(item);
    }

    public Result CancelItem(Guid tabItemId, string reason, Guid actorUserId, DateTimeOffset utcNow)
    {
        if (Status is DiningTabStatus.Closed or DiningTabStatus.Cancelled)
            return Result.Failure(Error.Conflict("Tab.NotOpen", "Comanda já encerrada."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Tab.CancelReasonRequired", "Motivo do cancelamento do item é obrigatório."));

        var item = _items.FirstOrDefault(i => i.Id == tabItemId);
        if (item is null)
            return Result.Failure(Error.NotFound("Tab.ItemNotFound", "Item não encontrado na comanda."));
        if (item.Status == DiningTabItemStatus.Cancelled)
            return Result.Failure(Error.Conflict("Tab.ItemAlreadyCancelled", "Item já cancelado."));

        item.Cancel(reason);
        RecalculateTotals();
        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(), TenantId, Id, "ItemCancelled",
            $"Item '{item.ItemTitle}' cancelado: {reason.Trim()}", actorUserId, utcNow));
        return Result.Success();
    }

    public Result TransferToTable(DiningTable? currentTable, DiningTable targetTable, Guid actorUserId, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(targetTable);

        if (Status is DiningTabStatus.Closed or DiningTabStatus.Cancelled)
            return Result.Failure(Error.Conflict("Tab.NotOpen", "Comanda encerrada não pode ser transferida."));
        if (targetTable.TenantId != TenantId)
            return Result.Failure(Error.Forbidden("Tab.CrossTenantTable", "Mesa de destino pertence a outro estabelecimento."));

        var occupyRes = targetTable.OccupyWithTab(Id);
        if (occupyRes.IsFailure)
            return occupyRes;

        currentTable?.ReleaseToAvailable();
        var oldTableCode = currentTable?.TableCode ?? "SemMesa";
        DiningTableId = targetTable.Id;

        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(), TenantId, Id, "TableTransferred",
            $"Comanda transferida de '{oldTableCode}' para mesa '{targetTable.TableCode}'.", actorUserId, utcNow));
        return Result.Success();
    }

    public Result UpdateServiceCharge(decimal newPercent, string reason, Guid actorUserId, DateTimeOffset utcNow)
    {
        if (newPercent is < 0m or > 30m)
            return Result.Failure(Error.Validation("Tab.InvalidServiceCharge", "Taxa de serviço deve estar entre 0% e 30%."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Tab.ServiceChargeReasonRequired", "Motivo da alteração de taxa de serviço é obrigatório."));

        ServiceChargePercent = decimal.Round(newPercent, 2, MidpointRounding.ToEven);
        RecalculateTotals();
        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(), TenantId, Id, "ServiceChargeChanged",
            $"Taxa de serviço ajustada para {ServiceChargePercent:N2}% ({reason.Trim()}).", actorUserId, utcNow));
        return Result.Success();
    }

    /// <summary>
    /// Divide igualmente o saldo restante (com centavos residuais ajustados na última parcela para soma exata).
    /// </summary>
    public Result<IReadOnlyList<TabSplitShare>> SplitRemainingEqually(int payersCount)
    {
        if (payersCount <= 0)
            return Result.Failure<IReadOnlyList<TabSplitShare>>(Error.Validation("Tab.InvalidPayersCount", "Quantidade de pagantes deve ser positiva."));

        var remaining = RemainingBalance;
        var currency = remaining.Currency;
        var totalCents = (long)decimal.Round(remaining.Amount * 100m, 0, MidpointRounding.ToEven);
        var baseCents = totalCents / payersCount;
        var remainderCents = totalCents % payersCount;

        var shares = new List<TabSplitShare>(payersCount);
        for (var i = 1; i <= payersCount; i++)
        {
            var shareCents = baseCents + (i <= remainderCents ? 1 : 0);
            shares.Add(new TabSplitShare(i, Money.Create(shareCents / 100m, currency).Value));
        }

        return Result.Success<IReadOnlyList<TabSplitShare>>(shares);
    }

    public Result RegisterPartialOrFullPayment(Money amount, Guid actorUserId, DiningTable? linkedTable, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (Status is DiningTabStatus.Closed or DiningTabStatus.Cancelled)
            return Result.Failure(Error.Conflict("Tab.AlreadyClosed", "Comanda já encerrada."));
        if (amount.Amount <= 0m)
            return Result.Failure(Error.Validation("Tab.InvalidPayment", "Valor do pagamento deve ser positivo."));
        if (amount.Amount > RemainingBalance.Amount)
            return Result.Failure(Error.Validation("Tab.Overpayment", $"Valor informado ({amount}) supera o saldo restante ({RemainingBalance})."));

        PaidAmount = PaidAmount.Add(amount);
        Status = RemainingBalance.Amount == 0m ? DiningTabStatus.Closed : DiningTabStatus.ClosingRequested;

        if (Status == DiningTabStatus.Closed)
        {
            ClosedAtUtc = utcNow;
            linkedTable?.ReleaseToAvailable();
        }
        else
        {
            linkedTable?.MarkAwaitingPayment();
        }

        _auditEvents.Add(new DiningTabAuditEvent(
            Guid.NewGuid(), TenantId, Id, "TabPaymentRecorded",
            $"Recebido {amount}. Saldo restante: {RemainingBalance}. Status: {Status}.", actorUserId, utcNow));
        return Result.Success();
    }

    private void RecalculateTotals()
    {
        var currency = ItemsSubtotal.Currency;
        var activeSub = Money.Zero(currency);
        foreach (var item in _items.Where(i => i.Status != DiningTabItemStatus.Cancelled))
            activeSub = activeSub.Add(item.LineSubtotal);

        ItemsSubtotal = activeSub;
        var serviceAmt = decimal.Round(activeSub.Amount * (ServiceChargePercent / 100m), 2, MidpointRounding.ToEven);
        ServiceChargeAmount = Money.Create(serviceAmt, currency).Value;
        TotalAmount = ItemsSubtotal.Add(ServiceChargeAmount);
    }
}
