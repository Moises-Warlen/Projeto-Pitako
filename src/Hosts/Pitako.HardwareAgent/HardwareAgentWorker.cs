using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.HardwareAgent;

/// <summary>
/// Transporte local ESC/POS via TCP RAW (ex.: porta 9100 em impressora térmica na LAN).
/// Retorna indisponibilidade honesta se o endpoint não estiver acessível.
/// </summary>
public sealed class LocalNetworkEscPosPrinterTransport : IThermalPrinterTransport
{
    public async Task<Result> SendEscPosAsync(
        PrinterProfile printer,
        byte[] escPosPayload,
        int copies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (printer.ConnectionKind != PrinterConnectionKind.NetworkTcpEscPos)
        {
            return Result.Failure(new Error(
                "HardwareAgent.SpoolerPortNotBound",
                $"Conexão '{printer.ConnectionKind}' em '{printer.EndpointOrPort}' requer spooler/COM local configurado no terminal."));
        }

        var parts = printer.EndpointOrPort.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            return Result.Failure(Error.Validation(
                "HardwareAgent.InvalidTcpEndpoint",
                "Endpoint TCP deve seguir o formato host:porta (ex.: 192.168.0.50:9100)."));
        }

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(parts[0], port, timeoutCts.Token);

            await using var stream = client.GetStream();
            for (var i = 0; i < Math.Max(1, copies); i++)
                await stream.WriteAsync(escPosPayload, timeoutCts.Token);

            await stream.FlushAsync(timeoutCts.Token);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "HardwareAgent.PrinterUnreachable",
                $"Impressora térmica '{printer.PrinterCode}' inacessível em {printer.EndpointOrPort}: {ex.Message}"));
        }
    }
}

/// <summary>
/// Transporte TCP local para balanças Toledo compatíveis com rede; portas seriais COM retornam fallback explícito quando desconectadas.
/// </summary>
public sealed class LocalNetworkToledoScaleTransport : IScaleTransport
{
    public async Task<Result<string>> ReadRawFrameAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (scale.ConnectionType != ScaleConnectionType.TcpIp)
        {
            return Result.Failure<string>(new Error(
                "HardwareAgent.SerialScaleNotAttached",
                $"Porta local '{scale.EndpointOrPort}' ({scale.ConnectionType}) requer vínculo físico ativo; utilize lançamento manual em contingência."));
        }

        var parts = scale.EndpointOrPort.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            return Result.Failure<string>(Error.Validation("HardwareAgent.InvalidScaleEndpoint", "Endpoint TCP da balança inválido."));
        }

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(parts[0], port, timeoutCts.Token);
            await using var stream = client.GetStream();
            var buf = new byte[64];
            var read = await stream.ReadAsync(buf, timeoutCts.Token);
            if (read <= 0)
            {
                return Result.Failure<string>(new Error("HardwareAgent.EmptyScaleFrame", "Balança não enviou quadro de peso."));
            }
            return Result.Success(System.Text.Encoding.ASCII.GetString(buf, 0, read));
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(new Error(
                "HardwareAgent.ScaleUnreachable",
                $"Balança Toledo '{scale.Name}' inacessível em {scale.EndpointOrPort}: {ex.Message}"));
        }
    }
}

public sealed class HardwareAgentWorker(
    ILogger<HardwareAgentWorker> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scalePort = configuration["Hardware:ScaleEndpoint"] ?? "COM3";
        var printerEndpoint = configuration["Hardware:PrinterEndpoint"] ?? "USB_BALCAO";
        var apiUrl = configuration["Agent:ApiBaseUrl"] ?? "http://localhost:5000";

        logger.LogInformation(
            "Pitako.HardwareAgent iniciado. API={ApiUrl}, BalancaConfigurada={ScalePort}, ImpressoraConfigurada={PrinterEndpoint}.",
            apiUrl, scalePort, printerEndpoint);

        // Mantém ciclo de heartbeat honesto até cancelamento sem simular leituras físicas.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogDebug("HardwareAgent aguardando comandos locais de pesagem Toledo ({ScalePort}) e impressão ESC/POS ({PrinterEndpoint}).", scalePort, printerEndpoint);
        }
    }
}
