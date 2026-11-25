using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.MasterPanel.Application.Services;

public sealed record MasterDashboardIndicators(
    int TotalCustomers,
    int ActiveSubscriptions,
    int TrialingSubscriptions,
    int PastDueSubscriptions,
    int SuspendedSubscriptions,
    decimal MonthlyRecurringRevenueEstimate,
    decimal PaidRevenueTotal,
    int OverdueInvoicesCount,
    int OpenSupportTicketsCount);

/// <summary>
/// Serviço de suporte, configurações globais e indicadores executivos do Painel Master (Seção 8).
/// </summary>
public sealed class MasterOperationsAndDashboardService
{
    private readonly IMasterCustomerRepository _customerRepo;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly ICommercialPlanRepository _planRepo;
    private readonly ISupportTicketRepository _ticketRepo;
    private readonly IGlobalSettingRepository _settingRepo;
    private readonly IClock _clock;

    public MasterOperationsAndDashboardService(
        IMasterCustomerRepository customerRepo,
        ISubscriptionRepository subscriptionRepo,
        ICommercialPlanRepository planRepo,
        ISupportTicketRepository ticketRepo,
        IGlobalSettingRepository settingRepo,
        IClock clock)
    {
        _customerRepo = customerRepo;
        _subscriptionRepo = subscriptionRepo;
        _planRepo = planRepo;
        _ticketRepo = ticketRepo;
        _settingRepo = settingRepo;
        _clock = clock;
    }

    public async Task<Result<SupportTicket>> OpenSupportTicketAsync(
        User actor,
        Guid customerAccountId,
        Guid? tenantId,
        string subject,
        string description,
        SupportTicketPriority priority = SupportTicketPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<SupportTicket>(guard.Error);

        var ticketResult = SupportTicket.Open(customerAccountId, tenantId, subject, description, priority);
        if (ticketResult.IsFailure)
            return ticketResult;

        await _ticketRepo.AddAsync(ticketResult.Value, cancellationToken);
        await _ticketRepo.SaveChangesAsync(cancellationToken);
        return ticketResult;
    }

    public async Task<Result> ResolveSupportTicketAsync(
        User actor,
        Guid ticketId,
        string resolutionNotes,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var ticket = await _ticketRepo.FindByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
            return Result.Failure(Error.NotFound("Support.TicketNotFound", "Chamado não encontrado."));

        ticket.AssignTo(actor.Id);
        ticket.Resolve(resolutionNotes, _clock.UtcNow);
        await _ticketRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<GlobalPlatformSetting>> UpsertGlobalSettingAsync(
        User actor,
        string key,
        string value,
        string? description = null,
        bool isSecret = false,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.Administer);
        if (guard.IsFailure)
            return Result.Failure<GlobalPlatformSetting>(guard.Error);

        if (string.IsNullOrWhiteSpace(key))
            return Result.Failure<GlobalPlatformSetting>(Error.Validation("Settings.KeyRequired", "Chave de configuração é obrigatória."));

        var normalizedKey = key.Trim().ToLowerInvariant();
        var existing = await _settingRepo.FindByKeyAsync(normalizedKey, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateValue(value, description);
            await _settingRepo.SaveChangesAsync(cancellationToken);
            return Result.Success(existing);
        }

        var created = new GlobalPlatformSetting(Guid.NewGuid(), normalizedKey, value, description, isSecret);
        await _settingRepo.AddAsync(created, cancellationToken);
        await _settingRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(created);
    }

    public async Task<Result<MasterDashboardIndicators>> GetOverviewIndicatorsAsync(
        User actor,
        CancellationToken cancellationToken = default)
    {
        var guard = MasterActorGuard.EnsureMasterPermission(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<MasterDashboardIndicators>(guard.Error);

        var customers = await _customerRepo.ListAllAsync(cancellationToken);
        var subscriptions = await _subscriptionRepo.ListAllAsync(cancellationToken);
        var plans = (await _planRepo.ListAllAsync(onlyActive: false, cancellationToken)).ToDictionary(p => p.Id);
        var openTickets = await _ticketRepo.ListOpenAsync(cancellationToken);

        decimal mrrEstimate = 0m;
        decimal paidTotal = 0m;
        var overdueCount = 0;

        foreach (var sub in subscriptions)
        {
            if (sub.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && plans.TryGetValue(sub.PlanId, out var plan))
            {
                var monthlyFactor = plan.BillingCycle switch
                {
                    BillingCycle.Quarterly => 3m,
                    BillingCycle.Semiannual => 6m,
                    BillingCycle.Annual => 12m,
                    _ => 1m
                };
                mrrEstimate += decimal.Round(plan.Price.Amount / monthlyFactor, 2, MidpointRounding.ToEven);
            }

            foreach (var inv in sub.Invoices)
            {
                if (inv.Status == InvoiceStatus.Paid)
                    paidTotal += inv.Amount.Amount;
                else if (inv.Status == InvoiceStatus.Overdue)
                    overdueCount++;
            }
        }

        return Result.Success(new MasterDashboardIndicators(
            customers.Count,
            subscriptions.Count(s => s.Status == SubscriptionStatus.Active),
            subscriptions.Count(s => s.Status == SubscriptionStatus.Trialing),
            subscriptions.Count(s => s.Status == SubscriptionStatus.PastDue),
            subscriptions.Count(s => s.Status == SubscriptionStatus.Suspended),
            mrrEstimate,
            paidTotal,
            overdueCount,
            openTickets.Count));
    }
}
