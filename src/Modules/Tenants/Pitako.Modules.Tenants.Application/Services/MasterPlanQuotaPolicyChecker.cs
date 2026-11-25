using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Application.Services;

/// <summary>
/// Adaptador que conecta a verificação de cotas de estabelecimentos e unidades ao motor de planos do MasterPanel.
/// </summary>
public sealed class MasterPlanQuotaPolicyChecker(PlanEntitlementEnforcementService enforcement) : IPlanQuotaPolicyChecker
{
    public async Task<Result> CheckCanCreateEstablishmentAsync(Guid customerAccountId, int currentCount, CancellationToken cancellationToken = default)
    {
        var res = await enforcement.CheckResourceAllocationAsync(customerAccountId, QuotaResourceKind.Establishments, currentCount, 1, cancellationToken);
        return res.IsSuccess ? Result.Success() : Result.Failure(res.Error);
    }

    public async Task<Result> CheckCanCreateUnitAsync(Guid customerAccountId, int currentCount, CancellationToken cancellationToken = default)
    {
        var res = await enforcement.CheckResourceAllocationAsync(customerAccountId, QuotaResourceKind.Units, currentCount, 1, cancellationToken);
        return res.IsSuccess ? Result.Success() : Result.Failure(res.Error);
    }
}
