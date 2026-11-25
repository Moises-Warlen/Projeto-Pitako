using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.MasterPanel.Application.Services;

public sealed record QuotaEvaluationResult(
    bool IsAllowed,
    bool IsUnlimited,
    int? MaxAllowed,
    int CurrentUsage,
    string? RecommendedUpgradePlanCode,
    string? RecommendedUpgradePlanName);

/// <summary>
/// Serviço de backend responsável por validar limites comerciais e módulos habilitados
/// conforme o plano contratado do cliente, incluindo planos ilimitados sem teto artificial (Seção 7).
/// </summary>
public sealed class PlanEntitlementEnforcementService
{
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly ICommercialPlanRepository _planRepo;
    private readonly IClock _clock;

    public PlanEntitlementEnforcementService(
        ISubscriptionRepository subscriptionRepo,
        ICommercialPlanRepository planRepo,
        IClock clock)
    {
        _subscriptionRepo = subscriptionRepo;
        _planRepo = planRepo;
        _clock = clock;
    }

    public async Task<Result<QuotaEvaluationResult>> CheckResourceAllocationAsync(
        Guid customerAccountId,
        QuotaResourceKind resourceKind,
        int currentUsage,
        int requestedIncrement = 1,
        CancellationToken cancellationToken = default)
    {
        var contextResult = await ResolveOperationalPlanAsync(customerAccountId, cancellationToken);
        if (contextResult.IsFailure)
            return Result.Failure<QuotaEvaluationResult>(contextResult.Error);

        var plan = contextResult.Value;
        var limit = plan.GetLimit(resourceKind);

        if (limit.CanAllocate(currentUsage, requestedIncrement))
        {
            return Result.Success(new QuotaEvaluationResult(
                true,
                limit.IsUnlimited,
                limit.MaxAllowed,
                currentUsage,
                null,
                null));
        }

        // Busca um plano ativo superior para orientar upgrade na resposta ao cliente.
        var allPlans = await _planRepo.ListAllAsync(onlyActive: true, cancellationToken);
        var upgradeCandidate = allPlans
            .Where(p => p.Id != plan.Id && p.IsPublic)
            .Where(p => p.GetLimit(resourceKind).CanAllocate(currentUsage, requestedIncrement))
            .OrderBy(p => p.GetLimit(resourceKind).IsUnlimited ? 1 : 0)
            .ThenBy(p => p.Price.Amount)
            .FirstOrDefault();

        var upgradeHint = upgradeCandidate is not null
            ? $" Faça upgrade para o plano '{upgradeCandidate.Name}' para ampliar seu limite."
            : " Entre em contato com o suporte para upgrade de plano.";

        return Result.Failure<QuotaEvaluationResult>(Error.LimitExceeded(
            "Plan.LimitExceeded",
            $"Limite do plano '{plan.Name}' atingido para {resourceKind}: máximo de {limit.MaxAllowed} (em uso: {currentUsage}).{upgradeHint}"));
    }

    public async Task<Result> CheckModuleAccessAsync(
        Guid customerAccountId,
        string moduleCode,
        CancellationToken cancellationToken = default)
    {
        var contextResult = await ResolveOperationalPlanAsync(customerAccountId, cancellationToken);
        if (contextResult.IsFailure)
            return Result.Failure(contextResult.Error);

        var plan = contextResult.Value;
        if (!plan.IsModuleEnabled(moduleCode))
        {
            return Result.Failure(Error.Forbidden(
                "Plan.ModuleNotIncluded",
                $"O módulo '{moduleCode}' não está habilitado no plano '{plan.Name}'. Considere realizar upgrade."));
        }

        return Result.Success();
    }

    private async Task<Result<CommercialPlan>> ResolveOperationalPlanAsync(Guid customerAccountId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepo.FindLatestByCustomerIdAsync(customerAccountId, cancellationToken);
        if (subscription is null)
            return Result.Failure<CommercialPlan>(Error.NotFound("Subscription.NotFound", "Nenhuma assinatura encontrada para este cliente."));

        if (!subscription.IsOperational(_clock.UtcNow))
            return Result.Failure<CommercialPlan>(Error.Forbidden("Subscription.SuspendedOrExpired", $"Assinatura não operacional (status: {subscription.Status})."));

        var plan = await _planRepo.FindByIdAsync(subscription.PlanId, cancellationToken);
        if (plan is null)
            return Result.Failure<CommercialPlan>(Error.NotFound("Plan.NotFound", "Plano da assinatura não encontrado."));

        return Result.Success(plan);
    }
}
