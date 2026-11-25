using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Application.Services;

public sealed record CourierRunsReport(
    Guid CourierId,
    string CourierName,
    int TotalRuns,
    int DeliveredStopsCount,
    int FailedStopsCount,
    decimal TotalEstimatedDistanceKm,
    Money TotalFreightFees,
    Money TotalCourierCommission);

/// <summary>
/// Caso de uso de entregadores, agrupamento e otimização de rotas, rastreamento GPS
/// em tempo real e relatório de corridas/comissões (Seção 20).
/// </summary>
public sealed class CourierDispatchAndGpsService
{
    private readonly IDeliveryCourierRepository _courierRepo;
    private readonly IDeliveryDispatchRunRepository _runRepo;
    private readonly ICourierGpsPingRepository _gpsRepo;
    private readonly ICourierGpsRealtimeNotifier _gpsNotifier;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public CourierDispatchAndGpsService(
        IDeliveryCourierRepository courierRepo,
        IDeliveryDispatchRunRepository runRepo,
        ICourierGpsPingRepository gpsRepo,
        ICourierGpsRealtimeNotifier gpsNotifier,
        ITenantContext tenantContext,
        IClock clock)
    {
        _courierRepo = courierRepo;
        _runRepo = runRepo;
        _gpsRepo = gpsRepo;
        _gpsNotifier = gpsNotifier;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<DeliveryCourier>> RegisterCourierAsync(
        User actor,
        string fullName,
        string phone,
        CourierVehicleType vehicleType,
        CourierCommissionMode commissionMode,
        decimal fixedCommissionAmount = 0m,
        decimal freightCommissionPercentage = 0m,
        Guid? linkedUserId = null,
        string? licensePlate = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DeliveryCourier>(guard.Error);

        var fixedRes = Money.Create(fixedCommissionAmount);
        if (fixedRes.IsFailure)
            return Result.Failure<DeliveryCourier>(fixedRes.Error);

        var courierRes = DeliveryCourier.Create(
            guard.Value,
            fullName,
            phone,
            vehicleType,
            commissionMode,
            fixedRes.Value,
            freightCommissionPercentage,
            linkedUserId,
            licensePlate);

        if (courierRes.IsFailure)
            return courierRes;

        await _courierRepo.AddAsync(courierRes.Value, cancellationToken);
        await _courierRepo.SaveChangesAsync(cancellationToken);
        return courierRes;
    }

    public async Task<Result<DeliveryDispatchRun>> CreateAndOptimizeDispatchRunAsync(
        User actor,
        Guid courierId,
        double storeOriginLatitude,
        double storeOriginLongitude,
        IEnumerable<DispatchStopInput> stops,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DeliveryDispatchRun>(guard.Error);

        var originRes = GeoCoordinate.Create(storeOriginLatitude, storeOriginLongitude);
        if (originRes.IsFailure)
            return Result.Failure<DeliveryDispatchRun>(originRes.Error);

        var courier = await _courierRepo.FindByIdAsync(courierId, cancellationToken);
        if (courier is null)
            return Result.Failure<DeliveryDispatchRun>(Error.NotFound("Courier.NotFound", "Entregador não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, courier);

        var seq = await _runRepo.CountAllAsync(cancellationToken) + 1;
        var runCode = $"ROTA-{seq:D4}";

        var runRes = DeliveryDispatchRun.CreateOptimized(
            guard.Value,
            courier,
            runCode,
            originRes.Value,
            stops,
            _clock.UtcNow,
            notes);

        if (runRes.IsFailure)
            return runRes;

        await _runRepo.AddAsync(runRes.Value, cancellationToken);
        await _runRepo.SaveChangesAsync(cancellationToken);
        return runRes;
    }

    public async Task<Result> StartRunAsync(User actor, Guid dispatchRunId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var run = await _runRepo.FindByIdAsync(dispatchRunId, cancellationToken);
        if (run is null)
            return Result.Failure(Error.NotFound("Dispatch.NotFound", "Corrida não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, run);

        var courier = await _courierRepo.FindByIdAsync(run.CourierId, cancellationToken);
        if (courier is null)
            return Result.Failure(Error.NotFound("Courier.NotFound", "Entregador vinculado não encontrado."));

        var ownershipErr = EnsureCourierSelfAccessIfCourierRole(actor, courier);
        if (ownershipErr is not null)
            return Result.Failure(ownershipErr);

        var startRes = run.StartRoute(_clock.UtcNow);
        if (startRes.IsFailure)
            return startRes;

        await _runRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DeliveryDispatchRun>> ResolveStopAsync(
        User actor,
        Guid dispatchRunId,
        Guid stopId,
        bool delivered,
        string? notesOrFailureReason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<DeliveryDispatchRun>(guard.Error);

        var run = await _runRepo.FindByIdAsync(dispatchRunId, cancellationToken);
        if (run is null)
            return Result.Failure<DeliveryDispatchRun>(Error.NotFound("Dispatch.NotFound", "Corrida não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, run);

        var courier = await _courierRepo.FindByIdAsync(run.CourierId, cancellationToken);
        if (courier is null)
            return Result.Failure<DeliveryDispatchRun>(Error.NotFound("Courier.NotFound", "Entregador não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, courier);

        var ownershipErr = EnsureCourierSelfAccessIfCourierRole(actor, courier);
        if (ownershipErr is not null)
            return Result.Failure<DeliveryDispatchRun>(ownershipErr);

        var res = run.ResolveStop(stopId, delivered, notesOrFailureReason, courier, _clock.UtcNow);
        if (res.IsFailure)
            return Result.Failure<DeliveryDispatchRun>(res.Error);

        await _runRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(run);
    }

    public async Task<Result<CourierGpsPing>> RecordGpsPingAsync(
        User actor,
        Guid courierId,
        double latitude,
        double longitude,
        Guid? activeDispatchRunId = null,
        decimal? speedKmh = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<CourierGpsPing>(guard.Error);

        var courier = await _courierRepo.FindByIdAsync(courierId, cancellationToken);
        if (courier is null)
            return Result.Failure<CourierGpsPing>(Error.NotFound("Courier.NotFound", "Entregador não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, courier);

        var ownershipErr = EnsureCourierSelfAccessIfCourierRole(actor, courier);
        if (ownershipErr is not null)
            return Result.Failure<CourierGpsPing>(ownershipErr);

        var utcNow = _clock.UtcNow;
        var pingRes = CourierGpsPing.Create(guard.Value, courier.Id, latitude, longitude, utcNow, activeDispatchRunId, speedKmh);
        if (pingRes.IsFailure)
            return pingRes;

        var locRes = courier.UpdateLocation(latitude, longitude, utcNow);
        if (locRes.IsFailure)
            return Result.Failure<CourierGpsPing>(locRes.Error);

        await _gpsRepo.AddAsync(pingRes.Value, cancellationToken);
        await _gpsRepo.SaveChangesAsync(cancellationToken);

        await _gpsNotifier.NotifyLocationAsync(new CourierGpsRealtimeEvent(
            guard.Value,
            courier.Id,
            activeDispatchRunId,
            latitude,
            longitude,
            speedKmh,
            utcNow), cancellationToken);

        return pingRes;
    }

    public async Task<Result<CourierRunsReport>> GetCourierRunsReportAsync(
        User actor,
        Guid courierId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<CourierRunsReport>(guard.Error);

        var courier = await _courierRepo.FindByIdAsync(courierId, cancellationToken);
        if (courier is null)
            return Result.Failure<CourierRunsReport>(Error.NotFound("Courier.NotFound", "Entregador não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, courier);

        var ownershipErr = EnsureCourierSelfAccessIfCourierRole(actor, courier);
        if (ownershipErr is not null)
            return Result.Failure<CourierRunsReport>(ownershipErr);

        var runs = await _runRepo.ListByCourierPeriodAsync(courierId, fromUtc, toUtc, cancellationToken);

        var deliveredCount = runs.Sum(r => r.Stops.Count(s => s.Status == DeliveryStopStatus.Delivered));
        var failedCount = runs.Sum(r => r.Stops.Count(s => s.Status == DeliveryStopStatus.Failed));
        var totalKm = runs.Sum(r => r.TotalEstimatedDistanceKm);
        var currency = courier.FixedCommission.Currency;
        var totalFreights = Money.Create(runs.SelectMany(r => r.Stops).Where(s => s.Status == DeliveryStopStatus.Delivered).Sum(s => s.DeliveryFeeSnapshot.Amount), currency).Value;
        var totalCommission = Money.Create(runs.SelectMany(r => r.Stops).Where(s => s.Status == DeliveryStopStatus.Delivered).Sum(s => s.CourierCommission.Amount), currency).Value;

        return Result.Success(new CourierRunsReport(
            courier.Id,
            courier.FullName,
            runs.Count,
            deliveredCount,
            failedCount,
            totalKm,
            totalFreights,
            totalCommission));
    }

    private static Error? EnsureCourierSelfAccessIfCourierRole(User actor, DeliveryCourier courier)
    {
        if (actor.Scope == UserScope.Establishment &&
            actor.EstablishmentRole == EstablishmentRole.Courier &&
            courier.LinkedUserId != actor.Id)
        {
            return Error.Forbidden("Courier.CannotOperateAnotherCourier", "Entregador só pode atualizar sua própria rota e localização.");
        }

        return null;
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar logística de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Delivery, action))
            return Result.Failure<Guid>(Error.Forbidden("Delivery.Forbidden", "Permissão insuficiente para operar entregadores e logística."));

        return Result.Success(currentTenantId);
    }
}
