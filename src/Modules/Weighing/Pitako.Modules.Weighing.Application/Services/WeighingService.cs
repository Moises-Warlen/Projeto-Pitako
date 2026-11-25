using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Weighing.Application.Services;

/// <summary>
/// Caso de uso de configuração de balanças, taras, leitura automática (Toledo),
/// lançamento manual em contingência e auditoria de pesagens (Seção 14).
/// </summary>
public sealed class WeighingService : IWeighingOrderAttachmentService
{
    private readonly IScaleDeviceRepository _scaleRepo;
    private readonly ITarePresetRepository _tareRepo;
    private readonly IWeighingRecordRepository _recordRepo;
    private readonly IScaleTransport _scaleTransport;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public WeighingService(
        IScaleDeviceRepository scaleRepo,
        ITarePresetRepository tareRepo,
        IWeighingRecordRepository recordRepo,
        IScaleTransport scaleTransport,
        ITenantContext tenantContext,
        IClock clock)
    {
        _scaleRepo = scaleRepo;
        _tareRepo = tareRepo;
        _recordRepo = recordRepo;
        _scaleTransport = scaleTransport;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<ScaleDeviceConfig>> RegisterScaleDeviceAsync(
        User actor,
        string name,
        ScaleManufacturer manufacturer,
        ScaleConnectionType connectionType,
        string endpointOrPort,
        string protocolProfile = "TOLEDO_PRT",
        int baudRate = 9600,
        bool allowManualFallback = true,
        bool isDefault = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<ScaleDeviceConfig>(guard.Error);

        var res = ScaleDeviceConfig.Create(
            guard.Value,
            name,
            manufacturer,
            connectionType,
            endpointOrPort,
            protocolProfile,
            baudRate,
            allowManualFallback,
            isDefault);

        if (res.IsFailure)
            return res;

        await _scaleRepo.AddAsync(res.Value, cancellationToken);
        await _scaleRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<TarePreset>> RegisterTarePresetAsync(
        User actor,
        string name,
        decimal tareWeightGrams,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<TarePreset>(guard.Error);

        var res = TarePreset.Create(guard.Value, name, tareWeightGrams);
        if (res.IsFailure)
            return res;

        await _tareRepo.AddAsync(res.Value, cancellationToken);
        await _tareRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    /// <summary>
    /// Realiza pesagem automática lendo o quadro da balança configurada, aplicando tara e calculando valor por kg.
    /// </summary>
    public async Task<Result<WeighingRecord>> CaptureAutomaticWeighingAsync(
        User actor,
        Guid scaleDeviceId,
        string productName,
        decimal pricePerKilogramAmount,
        Guid? productId = null,
        Guid? tarePresetId = null,
        decimal? manualTareGrams = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<WeighingRecord>(guard.Error);

        var scale = await _scaleRepo.FindByIdAsync(scaleDeviceId, cancellationToken);
        if (scale is null)
            return Result.Failure<WeighingRecord>(Error.NotFound("Scale.NotFound", "Balança não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, scale);
        if (!scale.IsActive)
            return Result.Failure<WeighingRecord>(Error.Conflict("Scale.Inactive", "Balança desativada."));

        var rawResult = await _scaleTransport.ReadRawFrameAsync(scale, cancellationToken);
        if (rawResult.IsFailure)
            return Result.Failure<WeighingRecord>(rawResult.Error);

        var parsedResult = ToledoScaleProtocolParser.ParseFrame(rawResult.Value);
        if (parsedResult.IsFailure)
            return Result.Failure<WeighingRecord>(parsedResult.Error);

        var tareResult = await ResolveTareGramsAsync(tarePresetId, manualTareGrams, cancellationToken);
        if (tareResult.IsFailure)
            return Result.Failure<WeighingRecord>(tareResult.Error);

        var priceRes = Money.Create(pricePerKilogramAmount);
        if (priceRes.IsFailure)
            return Result.Failure<WeighingRecord>(priceRes.Error);

        var recordRes = WeighingRecord.Create(
            guard.Value,
            actor.Id,
            productName,
            priceRes.Value,
            parsedResult.Value.GrossWeightGrams,
            tareResult.Value,
            WeighingCaptureMode.AutomaticScale,
            _clock.UtcNow,
            scale.Id,
            tarePresetId,
            productId,
            null,
            parsedResult.Value.NormalizedPayload);

        if (recordRes.IsFailure)
            return recordRes;

        await _recordRepo.AddAsync(recordRes.Value, cancellationToken);
        await _recordRepo.SaveChangesAsync(cancellationToken);
        return recordRes;
    }

    /// <summary>
    /// Contingência operacional: registra pesagem manual auditada quando a balança está indisponível.
    /// </summary>
    public async Task<Result<WeighingRecord>> RegisterManualWeighingAsync(
        User actor,
        string productName,
        decimal pricePerKilogramAmount,
        decimal grossWeightGrams,
        string manualReason,
        Guid? scaleDeviceId = null,
        Guid? productId = null,
        Guid? tarePresetId = null,
        decimal? manualTareGrams = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<WeighingRecord>(guard.Error);

        if (scaleDeviceId.HasValue && scaleDeviceId.Value != Guid.Empty)
        {
            var scale = await _scaleRepo.FindByIdAsync(scaleDeviceId.Value, cancellationToken);
            if (scale is null)
                return Result.Failure<WeighingRecord>(Error.NotFound("Scale.NotFound", "Balança não encontrada."));

            TenantGuard.EnsureOwnership(_tenantContext, scale);
            if (!scale.AllowManualFallback)
                return Result.Failure<WeighingRecord>(Error.Forbidden("Weighing.ManualDisabled", "Esta balança exige leitura automática e bloqueia lançamento manual."));
        }

        var tareResult = await ResolveTareGramsAsync(tarePresetId, manualTareGrams, cancellationToken);
        if (tareResult.IsFailure)
            return Result.Failure<WeighingRecord>(tareResult.Error);

        var priceRes = Money.Create(pricePerKilogramAmount);
        if (priceRes.IsFailure)
            return Result.Failure<WeighingRecord>(priceRes.Error);

        var recordRes = WeighingRecord.Create(
            guard.Value,
            actor.Id,
            productName,
            priceRes.Value,
            grossWeightGrams,
            tareResult.Value,
            WeighingCaptureMode.ManualFallback,
            _clock.UtcNow,
            scaleDeviceId,
            tarePresetId,
            productId,
            manualReason,
            null);

        if (recordRes.IsFailure)
            return recordRes;

        await _recordRepo.AddAsync(recordRes.Value, cancellationToken);
        await _recordRepo.SaveChangesAsync(cancellationToken);
        return recordRes;
    }

    public async Task<Result<WeighingRecord>> GetValidatedRecordAsync(
        Guid weighingRecordId,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var record = await _recordRepo.FindByIdAsync(weighingRecordId, cancellationToken);
        if (record is null)
            return Result.Failure<WeighingRecord>(Error.NotFound("Weighing.NotFound", "Registro de pesagem não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, record);
        return Result.Success(record);
    }

    public async Task<Result<WeighingRecord>> AttachToOrderAsync(
        User actor,
        Guid weighingRecordId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de vincular pesagem de outro estabelecimento.");

        var allowed = actor.HasPermission(SystemModules.Weighing, PermissionAction.Edit) ||
                      actor.HasPermission(SystemModules.Orders, PermissionAction.Create) ||
                      actor.HasPermission(SystemModules.Orders, PermissionAction.Edit);
        if (!allowed)
            return Result.Failure<WeighingRecord>(Error.Forbidden("Weighing.Forbidden", "Permissão insuficiente para vincular pesagem ao pedido."));

        var record = await _recordRepo.FindByIdAsync(weighingRecordId, cancellationToken);
        if (record is null)
            return Result.Failure<WeighingRecord>(Error.NotFound("Weighing.NotFound", "Registro de pesagem não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, record);

        var attachRes = record.AttachToOrder(orderId);
        if (attachRes.IsFailure)
            return Result.Failure<WeighingRecord>(attachRes.Error);

        await _recordRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(record);
    }

    private async Task<Result<decimal>> ResolveTareGramsAsync(
        Guid? tarePresetId,
        decimal? manualTareGrams,
        CancellationToken cancellationToken)
    {
        if (tarePresetId.HasValue && tarePresetId.Value != Guid.Empty)
        {
            var preset = await _tareRepo.FindByIdAsync(tarePresetId.Value, cancellationToken);
            if (preset is null)
                return Result.Failure<decimal>(Error.NotFound("Tare.NotFound", "Tara pré-cadastrada não encontrada."));

            TenantGuard.EnsureOwnership(_tenantContext, preset);
            if (!preset.IsActive)
                return Result.Failure<decimal>(Error.Conflict("Tare.Inactive", "Tara desativada."));

            return Result.Success(preset.TareWeightGrams);
        }

        return Result.Success(manualTareGrams ?? 0m);
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar balança/pesagem de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Weighing, action))
            return Result.Failure<Guid>(Error.Forbidden("Weighing.Forbidden", "Permissão insuficiente para operar pesagem e balança."));

        return Result.Success(currentTenantId);
    }
}
