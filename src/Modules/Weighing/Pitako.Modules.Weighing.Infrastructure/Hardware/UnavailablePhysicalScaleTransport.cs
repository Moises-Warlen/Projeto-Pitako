using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Weighing.Infrastructure.Hardware;

/// <summary>
/// Transporte padrão quando o servidor/estação não possui agente serial/USB ou balança física Toledo acoplada.
/// Nunca simula leitura falsa: retorna falha explícita orientando contingência manual auditada.
/// </summary>
public sealed class UnavailablePhysicalScaleTransport : IScaleTransport
{
    public Task<Result<string>> ReadRawFrameAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(Error.Conflict(
            "Scale.PhysicalHardwareNotConnected",
            $"Balança física '{scale.Name}' ({scale.EndpointOrPort}) não detectada neste ambiente; utilize lançamento manual em contingência.")));
}
