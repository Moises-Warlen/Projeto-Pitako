using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.PdvCashier.Domain;

public enum CashRegisterStatus
{
    Open = 1,
    Closed = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Pix = 2,
    DebitCard = 3,
    CreditCard = 4,
    MealVoucher = 5,
    Other = 9
}

public enum CashMovementType
{
    SalePayment = 1,
    CashWithdrawal = 2,
    CashSupply = 3,
    PaymentReversal = 4
}

/// <summary>
/// Movimento financeiro auditável dentro de um turno de caixa (Seção 23).
/// </summary>
public sealed class CashMovement : Entity<Guid>, ITenantScopedEntity
{
    private CashMovement()
    {
        Amount = Money.Zero();
        Reason = string.Empty;
    }

    internal CashMovement(
        Guid id,
        Guid tenantId,
        Guid cashRegisterSessionId,
        CashMovementType movementType,
        PaymentMethod paymentMethod,
        Money amount,
        string reason,
        Guid? pdvSaleId,
        Guid? orderId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc) : base(id)
    {
        TenantId = tenantId;
        CashRegisterSessionId = cashRegisterSessionId;
        MovementType = movementType;
        PaymentMethod = paymentMethod;
        Amount = Money.Create(amount.Amount, amount.Currency).Value;
        Reason = reason;
        PdvSaleId = pdvSaleId;
        OrderId = orderId;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid CashRegisterSessionId { get; private set; }
    public CashMovementType MovementType { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public Money Amount { get; private set; }
    public string Reason { get; private set; }
    public Guid? PdvSaleId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em CashMovement.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Sessão/turno de caixa do PDV com abertura, suprimentos, sangrias, pagamentos, estornos
/// e conferência automática de saldo esperado vs. contado no fechamento (Seção 23).
/// </summary>
public sealed class CashRegisterSession : TenantScopedAggregateRoot
{
    private readonly List<CashMovement> _movements = new();

    private CashRegisterSession()
    {
        TerminalCode = string.Empty;
        OpeningBalance = Money.Zero();
        ExpectedCashBalance = Money.Zero();
    }

    private CashRegisterSession(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string terminalCode,
        Guid openedByUserId,
        Money openingBalance,
        DateTimeOffset openedAtUtc) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        TerminalCode = terminalCode;
        OpenedByUserId = openedByUserId;
        OpeningBalance = Money.Create(openingBalance.Amount, openingBalance.Currency).Value;
        ExpectedCashBalance = Money.Create(openingBalance.Amount, openingBalance.Currency).Value;
        OpenedAtUtc = openedAtUtc;
        Status = CashRegisterStatus.Open;
    }

    public Guid? StoreUnitId { get; private set; }
    public string TerminalCode { get; private set; }
    public Guid OpenedByUserId { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public CashRegisterStatus Status { get; private set; }
    public Money OpeningBalance { get; private set; }
    public Money ExpectedCashBalance { get; private set; }
    public Money? CountedCashBalance { get; private set; }
    public Money? CashVariance { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? ClosingNotes { get; private set; }

    public IReadOnlyCollection<CashMovement> Movements => _movements.AsReadOnly();

    public static Result<CashRegisterSession> Open(
        Guid tenantId,
        string terminalCode,
        Guid openedByUserId,
        Money openingBalance,
        DateTimeOffset openedAtUtc,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CashRegisterSession>(Error.Validation("CashRegister.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(terminalCode))
            return Result.Failure<CashRegisterSession>(Error.Validation("CashRegister.TerminalRequired", "Código do terminal/caixa é obrigatório."));
        if (openedByUserId == Guid.Empty)
            return Result.Failure<CashRegisterSession>(Error.Validation("CashRegister.OperatorRequired", "Operador de abertura é obrigatório."));

        ArgumentNullException.ThrowIfNull(openingBalance);

        return Result.Success(new CashRegisterSession(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            terminalCode.Trim().ToUpperInvariant(),
            openedByUserId,
            openingBalance,
            openedAtUtc));
    }

    public Result<CashMovement> RegisterCashSupply(Money amount, string reason, Guid actorUserId, DateTimeOffset utcNow)
    {
        if (Status != CashRegisterStatus.Open)
            return Result.Failure<CashMovement>(Error.Conflict("CashRegister.SessionClosed", "Caixa fechado não aceita suprimento."));
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0m)
            return Result.Failure<CashMovement>(Error.Validation("CashRegister.InvalidSupplyAmount", "Valor do suprimento deve ser positivo."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<CashMovement>(Error.Validation("CashRegister.ReasonRequired", "Motivo do suprimento é obrigatório."));

        var movement = new CashMovement(
            Guid.NewGuid(), TenantId, Id, CashMovementType.CashSupply, PaymentMethod.Cash,
            amount, reason.Trim(), null, null, actorUserId, utcNow);
        _movements.Add(movement);
        RecalculateExpectedCash();
        return Result.Success(movement);
    }

    public Result<CashMovement> RegisterCashWithdrawal(Money amount, string reason, Guid actorUserId, DateTimeOffset utcNow)
    {
        if (Status != CashRegisterStatus.Open)
            return Result.Failure<CashMovement>(Error.Conflict("CashRegister.SessionClosed", "Caixa fechado não aceita sangria."));
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0m)
            return Result.Failure<CashMovement>(Error.Validation("CashRegister.InvalidWithdrawalAmount", "Valor da sangria deve ser positivo."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<CashMovement>(Error.Validation("CashRegister.ReasonRequired", "Motivo da sangria é obrigatório."));

        if (amount.Amount > ExpectedCashBalance.Amount)
        {
            return Result.Failure<CashMovement>(Error.Conflict(
                "CashRegister.InsufficientCashForWithdrawal",
                $"Saldo em espécie ({ExpectedCashBalance}) insuficiente para sangria de {amount}."));
        }

        var movement = new CashMovement(
            Guid.NewGuid(), TenantId, Id, CashMovementType.CashWithdrawal, PaymentMethod.Cash,
            amount, reason.Trim(), null, null, actorUserId, utcNow);
        _movements.Add(movement);
        RecalculateExpectedCash();
        return Result.Success(movement);
    }

    internal Result<CashMovement> RecordSalePayment(
        PaymentMethod method,
        Money netAmount,
        Guid pdvSaleId,
        Guid? orderId,
        Guid actorUserId,
        DateTimeOffset utcNow)
    {
        if (Status != CashRegisterStatus.Open)
            return Result.Failure<CashMovement>(Error.Conflict("CashRegister.SessionClosed", "Não é possível registrar pagamento em caixa fechado."));

        var movement = new CashMovement(
            Guid.NewGuid(), TenantId, Id, CashMovementType.SalePayment, method,
            netAmount, $"Recebimento PDV", pdvSaleId, orderId, actorUserId, utcNow);
        _movements.Add(movement);
        RecalculateExpectedCash();
        return Result.Success(movement);
    }

    internal Result RecordPaymentReversal(
        PaymentMethod method,
        Money amount,
        Guid pdvSaleId,
        Guid? orderId,
        string reason,
        Guid actorUserId,
        DateTimeOffset utcNow)
    {
        if (Status != CashRegisterStatus.Open)
            return Result.Failure(Error.Conflict("CashRegister.SessionClosed", "Não é possível estornar pagamento em caixa fechado."));

        var movement = new CashMovement(
            Guid.NewGuid(), TenantId, Id, CashMovementType.PaymentReversal, method,
            amount, reason, pdvSaleId, orderId, actorUserId, utcNow);
        _movements.Add(movement);
        RecalculateExpectedCash();
        return Result.Success();
    }

    public Result Close(Money countedCashBalance, Guid closedByUserId, string? closingNotes, DateTimeOffset closedAtUtc)
    {
        if (Status == CashRegisterStatus.Closed)
            return Result.Failure(Error.Conflict("CashRegister.AlreadyClosed", "Esta sessão de caixa já foi fechada."));
        if (closedByUserId == Guid.Empty)
            return Result.Failure(Error.Validation("CashRegister.OperatorRequired", "Operador de fechamento é obrigatório."));

        ArgumentNullException.ThrowIfNull(countedCashBalance);

        RecalculateExpectedCash();
        CountedCashBalance = Money.Create(countedCashBalance.Amount, countedCashBalance.Currency).Value;
        var varianceAmount = countedCashBalance.Amount - ExpectedCashBalance.Amount;
        CashVariance = Money.Create(varianceAmount, OpeningBalance.Currency, allowNegative: true).Value;
        ClosedByUserId = closedByUserId;
        ClosingNotes = string.IsNullOrWhiteSpace(closingNotes) ? null : closingNotes.Trim();
        ClosedAtUtc = closedAtUtc;
        Status = CashRegisterStatus.Closed;
        return Result.Success();
    }

    public Money GetNetTotalByMethod(PaymentMethod method)
    {
        var currency = OpeningBalance.Currency;
        decimal sum = 0m;
        foreach (var m in _movements.Where(x => x.PaymentMethod == method))
        {
            sum += m.MovementType switch
            {
                CashMovementType.SalePayment or CashMovementType.CashSupply => m.Amount.Amount,
                CashMovementType.CashWithdrawal or CashMovementType.PaymentReversal => -m.Amount.Amount,
                _ => 0m
            };
        }

        return Money.Create(sum, currency, allowNegative: true).Value;
    }

    private void RecalculateExpectedCash()
    {
        var netCashMovements = GetNetTotalByMethod(PaymentMethod.Cash);
        ExpectedCashBalance = Money.Create(OpeningBalance.Amount + netCashMovements.Amount, OpeningBalance.Currency, allowNegative: true).Value;
    }
}
