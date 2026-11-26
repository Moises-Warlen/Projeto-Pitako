using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Application.Services;

/// <summary>
/// Caso de uso de configuração do delivery próprio e cálculo autoritativo de frete no backend (Seção 19).
/// </summary>
public sealed class DeliveryPolicyAndFreightService : IDeliveryFreightQuoteService
{
    private readonly IDeliveryPolicyRepository _policyRepo;
    private readonly ITenantContext _tenantContext;

    public DeliveryPolicyAndFreightService(
        IDeliveryPolicyRepository policyRepo,
        ITenantContext tenantContext)
    {
        _policyRepo = policyRepo;
        _tenantContext = tenantContext;
    }

    public async Task<Result<DeliveryPolicy>> CreatePolicyAsync(
        User actor,
        double originLatitude,
        double originLongitude,
        decimal maxRadiusKm,
        FreightCalculationMode calculationMode,
        decimal baseFeeAmount,
        decimal includedKm = 0m,
        decimal feePerKmAmount = 0m,
        int baseEstimatedMinutes = 35,
        decimal minimumOrderAmount = 0m,
        decimal? freeShippingThresholdAmount = null,
        Guid? storeUnitId = null,
        bool isDeliveryEnabled = true,
        bool allowPickup = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DeliveryPolicy>(guard.Error);

        var baseFeeRes = Money.Create(baseFeeAmount);
        var perKmRes = Money.Create(feePerKmAmount);
        var minRes = Money.Create(minimumOrderAmount);
        if (baseFeeRes.IsFailure) return Result.Failure<DeliveryPolicy>(baseFeeRes.Error);
        if (perKmRes.IsFailure) return Result.Failure<DeliveryPolicy>(perKmRes.Error);
        if (minRes.IsFailure) return Result.Failure<DeliveryPolicy>(minRes.Error);

        Money? freeThreshold = null;
        if (freeShippingThresholdAmount.HasValue)
        {
            var freeRes = Money.Create(freeShippingThresholdAmount.Value);
            if (freeRes.IsFailure)
                return Result.Failure<DeliveryPolicy>(freeRes.Error);
            freeThreshold = freeRes.Value;
        }

        var res = DeliveryPolicy.Create(
            guard.Value,
            originLatitude,
            originLongitude,
            maxRadiusKm,
            calculationMode,
            baseFeeRes.Value,
            includedKm,
            perKmRes.Value,
            baseEstimatedMinutes,
            minRes.Value,
            freeThreshold,
            storeUnitId,
            isDeliveryEnabled,
            allowPickup);

        if (res.IsFailure)
            return res;

        await _policyRepo.AddAsync(res.Value, cancellationToken);
        await _policyRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result> AddDistanceTierAsync(
        User actor,
        Guid policyId,
        decimal minKm,
        decimal maxKm,
        decimal feeAmount,
        int estimatedMinutes,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);

        var feeRes = Money.Create(feeAmount);
        if (feeRes.IsFailure)
            return feeRes;

        var addRes = policy.AddDistanceTier(minKm, maxKm, feeRes.Value, estimatedMinutes);
        if (addRes.IsFailure)
            return addRes;

        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddZoneRuleAsync(
        User actor,
        Guid policyId,
        string zoneName,
        string? zipCodePrefix,
        decimal feeAmount,
        int estimatedMinutes,
        bool isBlocked = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);

        var feeRes = Money.Create(feeAmount);
        if (feeRes.IsFailure)
            return feeRes;

        var addRes = policy.AddZoneRule(zoneName, zipCodePrefix, feeRes.Value, estimatedMinutes, isBlocked);
        if (addRes.IsFailure)
            return addRes;

        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PauseDeliveryAsync(
        User actor,
        Guid policyId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);
        policy.PauseDelivery(reason);
        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResumeDeliveryAsync(
        User actor,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);
        policy.ResumeDelivery();
        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DeliveryPolicy>> UpdatePolicyAsync(
        User actor,
        Guid policyId,
        double originLatitude,
        double originLongitude,
        decimal maxRadiusKm,
        FreightCalculationMode calculationMode,
        decimal baseFeeAmount,
        decimal includedKm = 0m,
        decimal feePerKmAmount = 0m,
        int baseEstimatedMinutes = 35,
        decimal minimumOrderAmount = 0m,
        decimal? freeShippingThresholdAmount = null,
        bool isDeliveryEnabled = true,
        bool allowPickup = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<DeliveryPolicy>(guard.Error);

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure<DeliveryPolicy>(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);

        var baseFeeRes = Money.Create(baseFeeAmount);
        var perKmRes = Money.Create(feePerKmAmount);
        var minRes = Money.Create(minimumOrderAmount);
        if (baseFeeRes.IsFailure) return Result.Failure<DeliveryPolicy>(baseFeeRes.Error);
        if (perKmRes.IsFailure) return Result.Failure<DeliveryPolicy>(perKmRes.Error);
        if (minRes.IsFailure) return Result.Failure<DeliveryPolicy>(minRes.Error);

        Money? freeThreshold = null;
        if (freeShippingThresholdAmount.HasValue)
        {
            var freeRes = Money.Create(freeShippingThresholdAmount.Value);
            if (freeRes.IsFailure)
                return Result.Failure<DeliveryPolicy>(freeRes.Error);
            freeThreshold = freeRes.Value;
        }

        var res = policy.UpdateConfiguration(
            originLatitude,
            originLongitude,
            maxRadiusKm,
            calculationMode,
            baseFeeRes.Value,
            includedKm,
            perKmRes.Value,
            baseEstimatedMinutes,
            minRes.Value,
            freeThreshold,
            isDeliveryEnabled,
            allowPickup);

        if (res.IsFailure)
            return Result.Failure<DeliveryPolicy>(res.Error);

        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(policy);
    }

    public async Task<Result> RemoveDistanceTierAsync(User actor, Guid policyId, Guid tierId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);
        var res = policy.RemoveDistanceTier(tierId);
        if (res.IsFailure)
            return res;

        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RemoveZoneRuleAsync(User actor, Guid policyId, Guid zoneRuleId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var policy = await _policyRepo.FindByIdAsync(policyId, cancellationToken);
        if (policy is null)
            return Result.Failure(Error.NotFound("Delivery.PolicyNotFound", "Política de delivery não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);
        var res = policy.RemoveZoneRule(zoneRuleId);
        if (res.IsFailure)
            return res;

        await _policyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DeliveryFreightQuote>> QuoteFreightAsync(
        Guid? storeUnitId,
        DeliveryFreightQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);
        var policy = await _policyRepo.FindEffectivePolicyAsync(storeUnitId, cancellationToken);
        if (policy is null)
            return Result.Failure<DeliveryFreightQuote>(Error.NotFound("Delivery.PolicyNotConfigured", "Política de entrega não configurada para este estabelecimento."));

        TenantGuard.EnsureOwnership(_tenantContext, policy);
        return policy.QuoteFreight(request);
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de configurar entrega de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Delivery, action))
            return Result.Failure<Guid>(Error.Forbidden("Delivery.Forbidden", "Permissão insuficiente para configurar delivery e frete."));

        return Result.Success(currentTenantId);
    }
}
