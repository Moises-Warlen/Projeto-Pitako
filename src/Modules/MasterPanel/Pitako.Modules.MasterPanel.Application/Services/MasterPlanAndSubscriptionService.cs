using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.MasterPanel.Application.Services;

public sealed record CreatePlanCommand(
    string Code,
    string Name,
    string Description,
    decimal PriceAmount,
    string Currency,
    BillingCycle BillingCycle,
    int TrialDays,
    PlanQuotaConfiguration Quotas,
    IEnumerable<string> EnabledModules,
    IEnumerable<string>? EnabledFeatures = null,
    bool IsPublic = true);

public sealed record DelinquencyProcessingSummary(
    int NewlyOverdueInvoices,
    int SuspendedSubscriptions);

/// <summary>
/// Serviços administrativos do Painel Master para criação de planos, gestão de assinaturas,
/// cobrança de mensalidades, inadimplência, suspensão/reativação e upgrade/downgrade.
/// </summary>
public sealed class MasterPlanAndSubscriptionService
{
    private readonly ICommercialPlanRepository _planRepo;
    private readonly IMasterCustomerRepository _customerRepo;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IClock _clock;

    public MasterPlanAndSubscriptionService(
        ICommercialPlanRepository planRepo,
        IMasterCustomerRepository customerRepo,
        ISubscriptionRepository subscriptionRepo,
        IClock clock)
    {
        _planRepo = planRepo;
        _customerRepo = customerRepo;
        _subscriptionRepo = subscriptionRepo;
        _clock = clock;
    }

    public async Task<Result<CommercialPlan>> CreatePlanAsync(
        User actor,
        CreatePlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<CommercialPlan>(guard.Error);

        var priceResult = Money.Create(command.PriceAmount, command.Currency);
        if (priceResult.IsFailure)
            return Result.Failure<CommercialPlan>(priceResult.Error);

        var normalizedCode = command.Code.Trim().ToLowerInvariant();
        if (await _planRepo.FindByCodeAsync(normalizedCode, cancellationToken) is not null)
            return Result.Failure<CommercialPlan>(Error.Conflict("Plan.CodeAlreadyExists", $"Já existe um plano com o código '{normalizedCode}'."));

        var existingPlans = await _planRepo.ListAllAsync(false, cancellationToken);
        var trimmedPlanName = command.Name.Trim();
        if (existingPlans.Any(p => p.Name.Trim().Equals(trimmedPlanName, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure<CommercialPlan>(Error.Conflict("Plan.NameAlreadyExists", $"Já existe um plano com o nome '{trimmedPlanName}'."));

        var planResult = CommercialPlan.Create(
            normalizedCode,
            command.Name,
            command.Description,
            priceResult.Value,
            command.BillingCycle,
            command.TrialDays,
            command.Quotas,
            command.EnabledModules,
            command.EnabledFeatures,
            command.IsPublic);

        if (planResult.IsFailure)
            return planResult;

        await _planRepo.AddAsync(planResult.Value, cancellationToken);
        await _planRepo.SaveChangesAsync(cancellationToken);
        return planResult;
    }

    public async Task<Result<CommercialPlan>> UpdatePlanAsync(
        User actor,
        Guid planId,
        CreatePlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<CommercialPlan>(guard.Error);

        var plan = await _planRepo.FindByIdAsync(planId, cancellationToken);
        if (plan is null)
            return Result.Failure<CommercialPlan>(Error.NotFound("Plan.NotFound", "Plano não encontrado."));

        var priceResult = Money.Create(command.PriceAmount, command.Currency);
        if (priceResult.IsFailure)
            return Result.Failure<CommercialPlan>(priceResult.Error);

        var update = plan.UpdateConfiguration(
            command.Name,
            command.Description,
            priceResult.Value,
            command.BillingCycle,
            command.TrialDays,
            command.IsPublic,
            command.Quotas,
            command.EnabledModules,
            command.EnabledFeatures);

        if (update.IsFailure)
            return Result.Failure<CommercialPlan>(update.Error);

        await _planRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(plan);
    }

    public async Task<Result<(MasterCustomerAccount Customer, Subscription Subscription)>> CreateCustomerSubscriptionAsync(
        User actor,
        string customerName,
        string documentNumber,
        string billingEmail,
        string? phone,
        Guid planId,
        bool startWithTrial = true,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<(MasterCustomerAccount, Subscription)>(guard.Error);

        var plan = await _planRepo.FindByIdAsync(planId, cancellationToken);
        if (plan is null || !plan.IsActive)
            return Result.Failure<(MasterCustomerAccount, Subscription)>(Error.Validation("Plan.Unavailable", "Plano selecionado não existe ou está inativo."));

        var customerResult = MasterCustomerAccount.Create(customerName, documentNumber, billingEmail, phone);
        if (customerResult.IsFailure)
            return Result.Failure<(MasterCustomerAccount, Subscription)>(customerResult.Error);

        var customer = customerResult.Value;
        var existingCustomers = await _customerRepo.ListAllAsync(cancellationToken);
        var docDigits = new string(customer.DocumentNumber.Where(char.IsDigit).ToArray());
        var emailLower = customer.BillingEmail.Trim().ToLowerInvariant();
        if (existingCustomers.Any(c =>
            (!string.IsNullOrEmpty(docDigits) && new string(c.DocumentNumber.Where(char.IsDigit).ToArray()) == docDigits) ||
            c.BillingEmail.Trim().Equals(emailLower, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<(MasterCustomerAccount, Subscription)>(
                Error.Conflict("Customer.AlreadyExists", "Já existe um cliente/dono cadastrado com este CPF/CNPJ ou e-mail."));
        }

        var subscription = Subscription.Create(customer.Id, plan, _clock.UtcNow, startWithTrial);

        await _customerRepo.AddAsync(customer, cancellationToken);
        await _subscriptionRepo.AddAsync(subscription, cancellationToken);
        await _subscriptionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success((customer, subscription));
    }

    public async Task<Result<SubscriptionInvoice>> IssueInvoiceAsync(
        User actor,
        Guid subscriptionId,
        string referencePeriod,
        DateTimeOffset dueDateUtc,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<SubscriptionInvoice>(guard.Error);

        var subscription = await _subscriptionRepo.FindByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return Result.Failure<SubscriptionInvoice>(Error.NotFound("Subscription.NotFound", "Assinatura não encontrada."));

        var plan = await _planRepo.FindByIdAsync(subscription.PlanId, cancellationToken);
        if (plan is null)
            return Result.Failure<SubscriptionInvoice>(Error.NotFound("Plan.NotFound", "Plano vinculado à assinatura não encontrado."));

        var invoice = subscription.IssueInvoice(referencePeriod, plan.Price, dueDateUtc);
        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(invoice);
    }

    public async Task<Result> RegisterInvoicePaymentAsync(
        User actor,
        Guid subscriptionId,
        Guid invoiceId,
        string gatewayReference,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return guard;

        var subscription = await _subscriptionRepo.FindByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return Result.Failure(Error.NotFound("Subscription.NotFound", "Assinatura não encontrada."));

        var result = subscription.RegisterPayment(invoiceId, _clock.UtcNow, gatewayReference, paymentMethod);
        if (result.IsFailure)
            return result;

        var customer = await _customerRepo.FindByIdAsync(subscription.CustomerAccountId, cancellationToken);
        if (customer is not null && subscription.Status == SubscriptionStatus.Active)
            customer.Activate();

        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DelinquencyProcessingSummary>> ProcessDelinquencyAsync(
        User actor,
        int gracePeriodDaysBeforeSuspension,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Administer);
        if (guard.IsFailure)
            return Result.Failure<DelinquencyProcessingSummary>(guard.Error);

        var utcNow = _clock.UtcNow;
        var subscriptions = await _subscriptionRepo.ListAllAsync(cancellationToken);
        var newlyOverdue = 0;
        var suspendedCount = 0;

        foreach (var subscription in subscriptions)
        {
            newlyOverdue += subscription.RefreshOverdueInvoices(utcNow);

            if (subscription.Status == SubscriptionStatus.Trialing &&
                subscription.TrialEndsAtUtc.HasValue &&
                subscription.TrialEndsAtUtc.Value <= utcNow)
            {
                var suspendTrial = subscription.Suspend("Período de trial gratuito expirado", utcNow);
                if (suspendTrial.IsSuccess)
                {
                    suspendedCount++;
                    var customer = await _customerRepo.FindByIdAsync(subscription.CustomerAccountId, cancellationToken);
                    customer?.Suspend();
                }
                continue;
            }

            if (subscription.Status == SubscriptionStatus.PastDue)
            {
                var oldestOverdue = subscription.Invoices
                    .Where(i => i.Status == InvoiceStatus.Overdue)
                    .OrderBy(i => i.DueDateUtc)
                    .FirstOrDefault();

                if (oldestOverdue is not null && oldestOverdue.DueDateUtc.AddDays(gracePeriodDaysBeforeSuspension) <= utcNow)
                {
                    var suspend = subscription.Suspend("Suspensão automática por inadimplência", utcNow);
                    if (suspend.IsSuccess)
                    {
                        suspendedCount++;
                        var customer = await _customerRepo.FindByIdAsync(subscription.CustomerAccountId, cancellationToken);
                        customer?.Suspend();
                    }
                }
            }
        }

        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(new DelinquencyProcessingSummary(newlyOverdue, suspendedCount));
    }

    public async Task<Result> SuspendSubscriptionAsync(
        User actor,
        Guid subscriptionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Administer);
        if (guard.IsFailure)
            return guard;

        var subscription = await _subscriptionRepo.FindByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return Result.Failure(Error.NotFound("Subscription.NotFound", "Assinatura não encontrada."));

        var result = subscription.Suspend(reason, _clock.UtcNow);
        if (result.IsFailure)
            return result;

        var customer = await _customerRepo.FindByIdAsync(subscription.CustomerAccountId, cancellationToken);
        customer?.Suspend();

        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReactivateSubscriptionAsync(
        User actor,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Administer);
        if (guard.IsFailure)
            return guard;

        var subscription = await _subscriptionRepo.FindByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return Result.Failure(Error.NotFound("Subscription.NotFound", "Assinatura não encontrada."));

        var result = subscription.Reactivate(_clock.UtcNow);
        if (result.IsFailure)
            return result;

        var customer = await _customerRepo.FindByIdAsync(subscription.CustomerAccountId, cancellationToken);
        customer?.Activate();

        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ChangeSubscriptionPlanAsync(
        User actor,
        Guid subscriptionId,
        Guid targetPlanId,
        CurrentUsageSnapshot currentUsage,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var subscription = await _subscriptionRepo.FindByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return Result.Failure(Error.NotFound("Subscription.NotFound", "Assinatura não encontrada."));

        var targetPlan = await _planRepo.FindByIdAsync(targetPlanId, cancellationToken);
        if (targetPlan is null)
            return Result.Failure(Error.NotFound("Plan.NotFound", "Plano de destino não encontrado."));

        var result = subscription.ChangePlan(targetPlan, currentUsage, _clock.UtcNow);
        if (result.IsFailure)
            return result;

        await _subscriptionRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
