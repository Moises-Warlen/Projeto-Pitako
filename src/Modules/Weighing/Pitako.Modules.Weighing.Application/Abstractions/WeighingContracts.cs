using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Weighing.Application.Abstractions;

public interface IScaleDeviceRepository
{
    Task<ScaleDeviceConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScaleDeviceConfig>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITarePresetRepository
{
    Task<TarePreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TarePreset>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TarePreset tarePreset, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWeighingRecordRepository
{
    Task<WeighingRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(WeighingRecord record, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstração de transporte físico para leitura de balanças comerciais (Serial, USB HID, TCP/IP).
/// </summary>
public interface IScaleTransport
{
    Task<Result<string>> ReadRawFrameAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato público para consumo futuro pelo Carrinho e PDV ao vincular pesagens validadas (Fases 11 e 14).
/// </summary>
public interface IWeighingOrderAttachmentService
{
    Task<Result<WeighingRecord>> AttachToOrderAsync(
        User actor,
        Guid weighingRecordId,
        Guid orderId,
        CancellationToken cancellationToken = default);
}
