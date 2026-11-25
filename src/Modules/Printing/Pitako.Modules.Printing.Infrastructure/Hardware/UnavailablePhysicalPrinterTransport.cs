using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Printing.Infrastructure.Hardware;

/// <summary>
/// Transporte padrão quando o servidor em nuvem não possui impressora térmica TCP/USB/Serial física conectada.
/// Nunca simula impressão física concluída: registra falha explícita orientando agente local ou reimpressão (Seção 27).
/// </summary>
public sealed class UnavailablePhysicalPrinterTransport : IThermalPrinterTransport
{
    public Task<Result> SendEscPosAsync(
        PrinterProfile printer,
        byte[] escPosPayload,
        int copies,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(Error.Conflict(
            "Printer.PhysicalHardwareNotConnected",
            $"Impressora física '{printer.DisplayName}' ({printer.EndpointOrPort}) não detectada neste ambiente sem agente local de impressão.")));
}
