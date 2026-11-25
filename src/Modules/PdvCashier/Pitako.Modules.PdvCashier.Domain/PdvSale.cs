using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.PdvCashier.Domain;

public enum PdvSaleStatus
{
    Settled = 1,
    Cancelled = 2
}

public sealed record PdvPaymentAllocationInput(
    PaymentMethod Method,
    decimal AllocatedAmount,
    decimal? TenderedAmount = null,
    string? AuthorizationCode = null);

public sealed record PdvReceiptPaymentLine(PaymentMethod Method, Money Allocated, Money Tendered, Money Change);

/// <summary>
/// Dados prontos de comprovante não fiscal do PDV.
/// Deve receber logo e CNPJ oficiais da loja na impressão (Seções 23 e 26).
/// </summary>
public sealed record PdvReceiptSnapshot(
    Guid PdvSaleId,
    string ReceiptNumber,
    Guid? OrderId,
    Money GrossAmount,
    Money DiscountAmount,
    Money NetTotalAmount,
    IReadOnlyList<PdvReceiptPaymentLine> Payments,
    Money TotalChangeGiven,
    Guid OperatorUserId,
    DateTimeOffset SettledAtUtc);

/// <summary>
/// Parcela individual de pagamento em uma venda/recebimento do PDV (suporta múltiplos pagamentos e troco).
/// </summary>
public sealed class PdvSalePayment : Entity<Guid>, ITenantScopedEntity
{
    private PdvSalePayment()
    {
        AllocatedAmount = Money.Zero();
        TenderedAmount = Money.Zero();
        ChangeAmount = Money.Zero();
    }

    internal PdvSalePayment(
        Guid id,
        Guid tenantId,
        Guid pdvSaleId,
        PaymentMethod method,
        Money allocatedAmount,
        Money tenderedAmount,
        Money changeAmount,
        string? authorizationCode) : base(id)
    {
        TenantId = tenantId;
        PdvSaleId = pdvSaleId;
        Method = method;
        AllocatedAmount = Money.Create(allocatedAmount.Amount, allocatedAmount.Currency).Value;
        TenderedAmount = Money.Create(tenderedAmount.Amount, tenderedAmount.Currency).Value;
        ChangeAmount = Money.Create(changeAmount.Amount, changeAmount.Currency).Value;
        AuthorizationCode = authorizationCode;
    }

    public Guid TenantId { get; private set; }
    public Guid PdvSaleId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public Money AllocatedAmount { get; private set; }
    public Money TenderedAmount { get; private set; }
    public Money ChangeAmount { get; private set; }
    public string? AuthorizationCode { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em PdvSalePayment.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Lançamento financeiro/venda liquidada no PDV com múltiplos pagamentos,
/// descontos autorizados, cancelamento auditado e lançamento no turno de caixa (Seção 23).
/// </summary>
public sealed class PdvSale : TenantScopedAggregateRoot
{
    private readonly List<PdvSalePayment> _payments = new();

    private PdvSale()
    {
        ReceiptNumber = string.Empty;
        GrossAmount = Money.Zero();
        DiscountAmount = Money.Zero();
        NetAmount = Money.Zero();
    }

    private PdvSale(
        Guid id,
        Guid tenantId,
        Guid cashRegisterSessionId,
        Guid? orderId,
        string receiptNumber,
        Money grossAmount,
        Money discountAmount,
        string? discountReason,
        Guid? discountAuthorizedByUserId,
        Money netAmount,
        Guid operatorUserId,
        DateTimeOffset settledAtUtc,
        string? idempotencyKey) : base(id, tenantId)
    {
        CashRegisterSessionId = cashRegisterSessionId;
        OrderId = orderId;
        ReceiptNumber = receiptNumber;
        GrossAmount = grossAmount;
        DiscountAmount = discountAmount;
        DiscountReason = discountReason;
        DiscountAuthorizedByUserId = discountAuthorizedByUserId;
        NetAmount = netAmount;
        Status = PdvSaleStatus.Settled;
        OperatorUserId = operatorUserId;
        SettledAtUtc = settledAtUtc;
        IdempotencyKey = idempotencyKey;
    }

    public Guid CashRegisterSessionId { get; private set; }
    public Guid? OrderId { get; private set; }
    public string ReceiptNumber { get; private set; }
    public Money GrossAmount { get; private set; }
    public Money DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public Guid? DiscountAuthorizedByUserId { get; private set; }
    public Money NetAmount { get; private set; }
    public PdvSaleStatus Status { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public DateTimeOffset SettledAtUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public IReadOnlyCollection<PdvSalePayment> Payments => _payments.AsReadOnly();

    public static Result<PdvSale> CreateAndSettle(
        CashRegisterSession session,
        Guid? orderId,
        string receiptNumber,
        Money grossAmount,
        Money discountAmount,
        string? discountReason,
        Guid? discountAuthorizedByUserId,
        IReadOnlyList<PdvPaymentAllocationInput> payments,
        Guid operatorUserId,
        DateTimeOffset utcNow,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(grossAmount);
        ArgumentNullException.ThrowIfNull(discountAmount);
        ArgumentNullException.ThrowIfNull(payments);

        if (session.Status != CashRegisterStatus.Open)
            return Result.Failure<PdvSale>(Error.Conflict("CashRegister.SessionClosed", "Caixa fechado não pode liquidar vendas."));
        if (string.IsNullOrWhiteSpace(receiptNumber))
            return Result.Failure<PdvSale>(Error.Validation("PdvSale.ReceiptRequired", "Número do comprovante é obrigatório."));
        if (grossAmount.Amount <= 0m)
            return Result.Failure<PdvSale>(Error.Validation("PdvSale.InvalidGross", "Valor bruto da venda deve ser positivo."));
        if (discountAmount.Amount > grossAmount.Amount)
            return Result.Failure<PdvSale>(Error.Validation("PdvSale.ExcessiveDiscount", "Desconto não pode ultrapassar o valor bruto."));
        if (discountAmount.Amount > 0m && (string.IsNullOrWhiteSpace(discountReason) || !discountAuthorizedByUserId.HasValue || discountAuthorizedByUserId.Value == Guid.Empty))
            return Result.Failure<PdvSale>(Error.Validation("PdvSale.DiscountAuthorizationRequired", "Desconto no PDV exige motivo e usuário autorizador."));
        if (payments.Count == 0)
            return Result.Failure<PdvSale>(Error.Validation("PdvSale.PaymentsRequired", "Informe ao menos uma forma de pagamento."));

        var currency = grossAmount.Currency;
        var netAmount = Money.Create(grossAmount.Amount - discountAmount.Amount, currency).Value;

        var saleId = Guid.NewGuid();
        var salePayments = new List<PdvSalePayment>();
        var allocatedSum = 0m;

        foreach (var p in payments)
        {
            if (p.AllocatedAmount <= 0m)
                return Result.Failure<PdvSale>(Error.Validation("PdvSale.InvalidPaymentAmount", "Cada parcela de pagamento deve ser positiva."));

            var allocMoney = Money.Create(p.AllocatedAmount, currency).Value;
            var tenderedValue = p.TenderedAmount ?? p.AllocatedAmount;
            if (p.Method != PaymentMethod.Cash && tenderedValue != p.AllocatedAmount)
                return Result.Failure<PdvSale>(Error.Validation("PdvSale.ChangeOnlyAllowedInCash", "Troco/valor entregue diferente do alocado aplica-se apenas a pagamento em dinheiro."));
            if (tenderedValue < p.AllocatedAmount)
                return Result.Failure<PdvSale>(Error.Validation("PdvSale.InsufficientTendered", "Valor entregue em espécie não pode ser inferior ao alocado."));

            var tenderedMoney = Money.Create(tenderedValue, currency).Value;
            var changeMoney = Money.Create(tenderedValue - allocMoney.Amount, currency).Value;

            allocatedSum += allocMoney.Amount;
            salePayments.Add(new PdvSalePayment(
                Guid.NewGuid(),
                session.TenantId,
                saleId,
                p.Method,
                allocMoney,
                tenderedMoney,
                changeMoney,
                string.IsNullOrWhiteSpace(p.AuthorizationCode) ? null : p.AuthorizationCode.Trim()));
        }

        if (allocatedSum != netAmount.Amount)
        {
            return Result.Failure<PdvSale>(Error.Validation(
                "PdvSale.PaymentSumMismatch",
                $"Soma dos pagamentos ({allocatedSum:F2}) difere do valor líquido ({netAmount.Amount:F2})."));
        }

        var sale = new PdvSale(
            saleId,
            session.TenantId,
            session.Id,
            orderId == Guid.Empty ? null : orderId,
            receiptNumber.Trim().ToUpperInvariant(),
            grossAmount,
            discountAmount,
            string.IsNullOrWhiteSpace(discountReason) ? null : discountReason.Trim(),
            discountAuthorizedByUserId,
            netAmount,
            operatorUserId,
            utcNow,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim());

        sale._payments.AddRange(salePayments);

        foreach (var sp in salePayments)
        {
            var moveRes = session.RecordSalePayment(sp.Method, sp.AllocatedAmount, sale.Id, sale.OrderId, operatorUserId, utcNow);
            if (moveRes.IsFailure)
                return Result.Failure<PdvSale>(moveRes.Error);
        }

        return Result.Success(sale);
    }

    public Result CancelAndReverseInSession(CashRegisterSession session, Guid actorUserId, string reason, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (Status == PdvSaleStatus.Cancelled)
            return Result.Failure(Error.Conflict("PdvSale.AlreadyCancelled", "Venda PDV já foi cancelada."));
        if (session.Id != CashRegisterSessionId)
            return Result.Failure(Error.Conflict("PdvSale.SessionMismatch", "Estorno deve ocorrer na sessão de caixa correspondente."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("PdvSale.CancellationReasonRequired", "Motivo do cancelamento é obrigatório."));

        foreach (var sp in _payments)
        {
            var revRes = session.RecordPaymentReversal(sp.Method, sp.AllocatedAmount, Id, OrderId, $"Cancelamento {ReceiptNumber}: {reason.Trim()}", actorUserId, utcNow);
            if (revRes.IsFailure)
                return revRes;
        }

        Status = PdvSaleStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledByUserId = actorUserId;
        CancelledAtUtc = utcNow;
        return Result.Success();
    }

    public PdvReceiptSnapshot BuildReceiptSnapshot()
    {
        var paymentLines = _payments
            .Select(p => new PdvReceiptPaymentLine(p.Method, p.AllocatedAmount, p.TenderedAmount, p.ChangeAmount))
            .ToList();

        var totalChange = Money.Create(_payments.Sum(p => p.ChangeAmount.Amount), NetAmount.Currency).Value;

        return new PdvReceiptSnapshot(
            Id,
            ReceiptNumber,
            OrderId,
            GrossAmount,
            DiscountAmount,
            NetAmount,
            paymentLines,
            totalChange,
            OperatorUserId,
            SettledAtUtc);
    }
}
