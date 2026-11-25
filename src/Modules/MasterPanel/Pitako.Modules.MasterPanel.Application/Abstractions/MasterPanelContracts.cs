using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.MasterPanel.Application.Abstractions;

public interface ICommercialPlanRepository
{
    Task<CommercialPlan?> FindByIdAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<CommercialPlan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommercialPlan>> ListAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default);
    Task AddAsync(CommercialPlan plan, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMasterCustomerRepository
{
    Task<MasterCustomerAccount?> FindByIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MasterCustomerAccount>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MasterCustomerAccount customer, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISubscriptionRepository
{
    Task<Subscription?> FindByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<Subscription?> FindLatestByCustomerIdAsync(Guid customerAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subscription>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISupportTicketRepository
{
    Task<SupportTicket?> FindByIdAsync(Guid ticketId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupportTicket>> ListOpenAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IGlobalSettingRepository
{
    Task<GlobalPlatformSetting?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlobalPlatformSetting>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(GlobalPlatformSetting setting, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Guarda centralizada garantindo que apenas usuários SaaS autorizados operem o Painel Master (Seção 10).
/// </summary>
public static class MasterActorGuard
{
    public static Result EnsureMasterPermission(User actor, PermissionAction requiredAction, string moduleCode = SystemModules.MasterPanel)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Scope != UserScope.Saas)
            return Result.Failure(Error.Forbidden("MasterPanel.EstablishmentForbidden", "Usuários de estabelecimentos não podem acessar o Painel Master."));

        if (!actor.HasPermission(moduleCode, requiredAction))
            return Result.Failure(Error.Forbidden("MasterPanel.InsufficientPermission", "Permissão insuficiente no Painel Master para esta operação."));

        return Result.Success();
    }
}
