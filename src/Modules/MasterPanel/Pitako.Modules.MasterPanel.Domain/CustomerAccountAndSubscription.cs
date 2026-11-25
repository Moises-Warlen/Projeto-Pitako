using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.MasterPanel.Domain;

public enum CustomerAccountStatus
{
    Active = 1,
    Suspended = 2,
    Cancelled = 3
}

public enum SubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    PastDue = 3,
    Suspended = 4,
    Cancelled = 5
}

public enum InvoiceStatus
{
    Pending = 1,
    Paid = 2,
    Overdue = 3,
    Voided = 4
}

/// <summary>
/// Instantâneo do consumo atual de recursos de um cliente para validar compatibilidade de downgrade (Seção 35).
/// </summary>
public sealed record CurrentUsageSnapshot(
    int EstablishmentsCount,
    int UnitsCount,
    int MarketplaceStoresCount,
    int UsersCount,
    int DevicesCount,
    int ConcurrentSessionsCount);

/// <summary>
/// Conta cliente contratante do SaaS administrada pelo Painel Master.
/// </summary>
public sealed class MasterCustomerAccount : AggregateRoot<Guid>, IAuditableEntity
{
    private MasterCustomerAccount()
    {
        Name = string.Empty;
        DocumentNumber = string.Empty;
        BillingEmail = string.Empty;
    }

    private MasterCustomerAccount(Guid id, string name, string documentNumber, string billingEmail, string? phone)
        : base(id)
    {
        Name = name;
        DocumentNumber = documentNumber;
        BillingEmail = billingEmail;
        Phone = phone;
        Status = CustomerAccountStatus.Active;
    }

    public string Name { get; private set; }
    public string DocumentNumber { get; private set; }
    public string BillingEmail { get; private set; }
    public string? Phone { get; private set; }
    public CustomerAccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static Result<MasterCustomerAccount> Create(string name, string documentNumber, string billingEmail, string? phone = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<MasterCustomerAccount>(Error.Validation("Customer.NameRequired", "Nome do cliente SaaS é obrigatório."));

        var digits = new string((documentNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14))
            return Result.Failure<MasterCustomerAccount>(Error.Validation("Customer.InvalidDocument", "Documento deve ser CPF (11) ou CNPJ (14) válido."));

        if (digits.Length == 14)
        {
            var cnpj = Cnpj.Create(digits);
            if (cnpj.IsFailure)
                return Result.Failure<MasterCustomerAccount>(cnpj.Error);
        }

        if (string.IsNullOrWhiteSpace(billingEmail) || !billingEmail.Contains('@'))
            return Result.Failure<MasterCustomerAccount>(Error.Validation("Customer.InvalidEmail", "E-mail financeiro inválido."));

        return Result.Success(new MasterCustomerAccount(
            Guid.NewGuid(),
            name.Trim(),
            digits,
            billingEmail.Trim().ToLowerInvariant(),
            phone?.Trim()));
    }

    public Result UpdateDetails(
        string name,
        string documentNumber,
        string billingEmail,
        string? phone,
        CustomerAccountStatus status)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Customer.NameRequired", "Nome do cliente SaaS é obrigatório."));

        var digits = new string((documentNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14))
            return Result.Failure(Error.Validation("Customer.InvalidDocument", "Documento deve ser CPF (11) ou CNPJ (14) válido."));

        if (digits.Length == 14)
        {
            var cnpj = Cnpj.Create(digits);
            if (cnpj.IsFailure)
                return Result.Failure(cnpj.Error);
        }

        if (string.IsNullOrWhiteSpace(billingEmail) || !billingEmail.Contains('@'))
            return Result.Failure(Error.Validation("Customer.InvalidEmail", "E-mail financeiro inválido."));

        Name = name.Trim();
        DocumentNumber = digits;
        BillingEmail = billingEmail.Trim().ToLowerInvariant();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Status = status;
        return Result.Success();
    }

    public void Suspend() => Status = CustomerAccountStatus.Suspended;
    public void Activate() => Status = CustomerAccountStatus.Active;
    public void Cancel() => Status = CustomerAccountStatus.Cancelled;

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}

public sealed class SubscriptionInvoice : Entity<Guid>
{
    private SubscriptionInvoice()
    {
        ReferencePeriod = string.Empty;
        Amount = Money.Zero();
    }

    internal SubscriptionInvoice(
        Guid id,
        Guid subscriptionId,
        string referencePeriod,
        Money amount,
        DateTimeOffset dueDateUtc) : base(id)
    {
        SubscriptionId = subscriptionId;
        ReferencePeriod = referencePeriod;
        Amount = amount;
        DueDateUtc = dueDateUtc;
        Status = InvoiceStatus.Pending;
    }

    public Guid SubscriptionId { get; private set; }
    public string ReferencePeriod { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset DueDateUtc { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public string? PaymentGatewayReference { get; private set; }
    public string? PaymentMethod { get; private set; }

    internal Result MarkPaid(DateTimeOffset paidAtUtc, string gatewayReference, string paymentMethod)
    {
        if (Status == InvoiceStatus.Voided)
            return Result.Failure(Error.Conflict("Invoice.Voided", "Mensalidade cancelada não pode ser paga."));

        if (Status == InvoiceStatus.Paid)
            return Result.Failure(Error.Conflict("Invoice.AlreadyPaid", "Mensalidade já quitada."));

        Status = InvoiceStatus.Paid;
        PaidAtUtc = paidAtUtc;
        PaymentGatewayReference = gatewayReference;
        PaymentMethod = paymentMethod;
        return Result.Success();
    }

    internal bool MarkOverdueIfExpired(DateTimeOffset utcNow)
    {
        if (Status == InvoiceStatus.Pending && DueDateUtc < utcNow)
        {
            Status = InvoiceStatus.Overdue;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Assinatura comercial de um cliente no PITAKO, controlando ciclo, inadimplência,
/// suspensão/reativação e troca de plano (upgrade/downgrade).
/// </summary>
public sealed class Subscription : AggregateRoot<Guid>, IAuditableEntity
{
    private readonly List<SubscriptionInvoice> _invoices = new();

    private Subscription()
    {
    }

    private Subscription(
        Guid id,
        Guid customerAccountId,
        Guid planId,
        SubscriptionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? trialEndsAtUtc,
        DateTimeOffset currentPeriodStartUtc,
        DateTimeOffset currentPeriodEndUtc) : base(id)
    {
        CustomerAccountId = customerAccountId;
        PlanId = planId;
        Status = status;
        StartedAtUtc = startedAtUtc;
        TrialEndsAtUtc = trialEndsAtUtc;
        CurrentPeriodStartUtc = currentPeriodStartUtc;
        CurrentPeriodEndUtc = currentPeriodEndUtc;
    }

    public Guid CustomerAccountId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? TrialEndsAtUtc { get; private set; }
    public DateTimeOffset CurrentPeriodStartUtc { get; private set; }
    public DateTimeOffset CurrentPeriodEndUtc { get; private set; }
    public DateTimeOffset? SuspendedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SubscriptionInvoice> Invoices => _invoices.AsReadOnly();

    public static Subscription Create(Guid customerAccountId, CommercialPlan plan, DateTimeOffset utcNow, bool startWithTrial = true)
    {
        if (customerAccountId == Guid.Empty)
            throw new ArgumentException("CustomerAccountId é obrigatório.", nameof(customerAccountId));

        ArgumentNullException.ThrowIfNull(plan);

        var hasTrial = startWithTrial && plan.TrialDays > 0;
        var status = hasTrial ? SubscriptionStatus.Trialing : SubscriptionStatus.Active;
        DateTimeOffset? trialEnds = hasTrial ? utcNow.AddDays(plan.TrialDays) : null;
        var periodEnd = CalculatePeriodEnd(utcNow, plan.BillingCycle);

        return new Subscription(
            Guid.NewGuid(),
            customerAccountId,
            plan.Id,
            status,
            utcNow,
            trialEnds,
            utcNow,
            periodEnd);
    }

    /// <summary>
    /// Verifica se a assinatura permite operação normal nos estabelecimentos do cliente.
    /// </summary>
    public bool IsOperational(DateTimeOffset utcNow)
    {
        if (Status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
            return false;

        if (Status == SubscriptionStatus.Trialing && TrialEndsAtUtc.HasValue && TrialEndsAtUtc.Value <= utcNow)
            return false;

        return true;
    }

    public SubscriptionInvoice IssueInvoice(string referencePeriod, Money amount, DateTimeOffset dueDateUtc)
    {
        var invoice = new SubscriptionInvoice(Guid.NewGuid(), Id, referencePeriod.Trim(), amount, dueDateUtc);
        _invoices.Add(invoice);
        return invoice;
    }

    public Result RegisterPayment(Guid invoiceId, DateTimeOffset paidAtUtc, string gatewayReference, string paymentMethod)
    {
        var invoice = _invoices.FirstOrDefault(i => i.Id == invoiceId);
        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Mensalidade não encontrada."));

        var payResult = invoice.MarkPaid(paidAtUtc, gatewayReference, paymentMethod);
        if (payResult.IsFailure)
            return payResult;

        // Reativa automaticamente quando não há outras faturas vencidas e a assinatura estava inadimplente ou suspensa por cobrança.
        var hasRemainingOverdue = _invoices.Any(i => i.Status == InvoiceStatus.Overdue);
        if (!hasRemainingOverdue && Status is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended or SubscriptionStatus.Trialing)
        {
            Status = SubscriptionStatus.Active;
            SuspendedAtUtc = null;
            SuspensionReason = null;
        }

        return Result.Success();
    }

    public int RefreshOverdueInvoices(DateTimeOffset utcNow)
    {
        var changed = 0;
        foreach (var invoice in _invoices)
        {
            if (invoice.MarkOverdueIfExpired(utcNow))
                changed++;
        }

        if (_invoices.Any(i => i.Status == InvoiceStatus.Overdue) && Status == SubscriptionStatus.Active)
            Status = SubscriptionStatus.PastDue;

        return changed;
    }

    public Result Suspend(string reason, DateTimeOffset utcNow)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(Error.Conflict("Subscription.Cancelled", "Assinatura cancelada não pode ser suspensa."));

        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = utcNow;
        SuspensionReason = string.IsNullOrWhiteSpace(reason) ? "Suspensão administrativa" : reason.Trim();
        return Result.Success();
    }

    public Result Reactivate(DateTimeOffset utcNow)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(Error.Conflict("Subscription.Cancelled", "Assinatura cancelada não pode ser reativada."));

        if (_invoices.Any(i => i.Status == InvoiceStatus.Overdue))
            return Result.Failure(Error.Conflict("Subscription.HasOverdueInvoices", "Existem mensalidades vencidas em aberto impedindo reativação."));

        Status = SubscriptionStatus.Active;
        SuspendedAtUtc = null;
        SuspensionReason = null;
        if (CurrentPeriodEndUtc <= utcNow)
            CurrentPeriodStartUtc = utcNow;

        return Result.Success();
    }

    public void Cancel(DateTimeOffset utcNow)
    {
        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = utcNow;
    }

    /// <summary>
    /// Executa upgrade ou downgrade validando se o uso atual cabe nos limites do plano de destino.
    /// </summary>
    public Result ChangePlan(CommercialPlan targetPlan, CurrentUsageSnapshot currentUsage, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(targetPlan);
        ArgumentNullException.ThrowIfNull(currentUsage);

        if (!targetPlan.IsActive)
            return Result.Failure(Error.Validation("Plan.Inactive", "O plano de destino está inativo."));

        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(Error.Conflict("Subscription.Cancelled", "Assinatura cancelada não permite troca de plano."));

        var violations = new List<string>();
        CheckTargetQuota(targetPlan.EstablishmentsLimit, currentUsage.EstablishmentsCount, "estabelecimentos", violations);
        CheckTargetQuota(targetPlan.UnitsLimit, currentUsage.UnitsCount, "unidades", violations);
        CheckTargetQuota(targetPlan.MarketplaceStoresLimit, currentUsage.MarketplaceStoresCount, "lojas em marketplaces", violations);
        CheckTargetQuota(targetPlan.UsersLimit, currentUsage.UsersCount, "usuários", violations);
        CheckTargetQuota(targetPlan.DevicesLimit, currentUsage.DevicesCount, "dispositivos", violations);
        CheckTargetQuota(targetPlan.ConcurrentSessionsLimit, currentUsage.ConcurrentSessionsCount, "sessões simultâneas", violations);

        if (violations.Count > 0)
        {
            return Result.Failure(Error.LimitExceeded(
                "Plan.DowngradeBlockedByCurrentUsage",
                $"Não é possível migrar para o plano '{targetPlan.Name}' porque o consumo atual excede os limites em: {string.Join(", ", violations)}."));
        }

        PlanId = targetPlan.Id;
        CurrentPeriodStartUtc = utcNow;
        CurrentPeriodEndUtc = CalculatePeriodEnd(utcNow, targetPlan.BillingCycle);
        return Result.Success();
    }

    private static void CheckTargetQuota(QuotaLimit targetLimit, int currentUsage, string label, List<string> violations)
    {
        if (!targetLimit.IsUnlimited && currentUsage > targetLimit.MaxAllowed!.Value)
            violations.Add($"{label} ({currentUsage}/{targetLimit.MaxAllowed.Value})");
    }

    private static DateTimeOffset CalculatePeriodEnd(DateTimeOffset start, BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => start.AddMonths(1),
        BillingCycle.Quarterly => start.AddMonths(3),
        BillingCycle.Semiannual => start.AddMonths(6),
        BillingCycle.Annual => start.AddYears(1),
        _ => start.AddMonths(1)
    };

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}
