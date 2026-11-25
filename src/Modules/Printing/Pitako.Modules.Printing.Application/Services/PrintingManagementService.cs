using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Printing.Application.Services;

public sealed record RoutedOrderItemInput(
    ProductionSectorType TargetSector,
    Guid? CategoryId,
    string ItemTitle,
    int Quantity,
    string CustomizationSummary,
    string? CustomerNotes = null,
    decimal? LineSubtotalAmount = null);

public sealed record RouteOrderTicketsRequest(
    string OrderOrTabReference,
    string ChannelName,
    string CustomerName,
    string? TableOrCallCode,
    string? StaffOperatorName,
    IReadOnlyList<RoutedOrderItemInput> Items,
    bool AlsoPrintCounterConference = false,
    decimal? OrderTotalAmount = null);

/// <summary>
/// Serviço de impressão por setores de produção com múltiplos dispositivos, fallback automático
/// entre impressora principal/reserva, cabeçalho obrigatório de logo + CNPJ da loja e fila auditável (Seção 27).
/// </summary>
public sealed class PrintingManagementService
{
    private readonly IPrinterProfileRepository _printerRepo;
    private readonly ISectorRoutingRuleRepository _routingRepo;
    private readonly IPrintJobRepository _jobRepo;
    private readonly IStorePrintIdentityProvider _storeIdentityProvider;
    private readonly IThermalPrinterTransport _printerTransport;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public PrintingManagementService(
        IPrinterProfileRepository printerRepo,
        ISectorRoutingRuleRepository routingRepo,
        IPrintJobRepository jobRepo,
        IStorePrintIdentityProvider storeIdentityProvider,
        IThermalPrinterTransport printerTransport,
        ITenantContext tenantContext,
        IClock clock)
    {
        _printerRepo = printerRepo;
        _routingRepo = routingRepo;
        _jobRepo = jobRepo;
        _storeIdentityProvider = storeIdentityProvider;
        _printerTransport = printerTransport;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<PrinterProfile>> RegisterPrinterAsync(
        User actor,
        string printerCode,
        string displayName,
        PrinterConnectionKind connectionKind,
        string endpointOrPort,
        PaperWidthMm paperWidthMm = PaperWidthMm.Mm80,
        Guid? backupPrinterId = null,
        bool supportsLogoRaster = true,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<PrinterProfile>(guard.Error);

        var normalizedCode = printerCode.Trim().ToUpperInvariant();
        if (await _printerRepo.FindByCodeAsync(normalizedCode, cancellationToken) is not null)
            return Result.Failure<PrinterProfile>(Error.Conflict("Printer.DuplicateCode", $"Impressora '{normalizedCode}' já cadastrada."));

        if (backupPrinterId.HasValue && backupPrinterId.Value != Guid.Empty)
        {
            var backup = await _printerRepo.FindByIdAsync(backupPrinterId.Value, cancellationToken);
            if (backup is null)
                return Result.Failure<PrinterProfile>(Error.NotFound("Printer.BackupNotFound", "Impressora reserva não encontrada."));
            TenantGuard.EnsureOwnership(_tenantContext, backup);
        }

        var res = PrinterProfile.Create(
            guard.Value,
            normalizedCode,
            displayName,
            connectionKind,
            endpointOrPort,
            paperWidthMm,
            backupPrinterId,
            supportsLogoRaster,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _printerRepo.AddAsync(res.Value, cancellationToken);
        await _printerRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<PrinterProfile>> UpdatePrinterStatusAsync(
        User actor,
        Guid printerId,
        PrinterStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<PrinterProfile>(guard.Error);

        var printer = await _printerRepo.FindByIdAsync(printerId, cancellationToken);
        if (printer is null)
            return Result.Failure<PrinterProfile>(Error.NotFound("Printer.NotFound", "Impressora não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, printer);
        printer.SetStatus(newStatus);
        await _printerRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(printer);
    }

    public async Task<Result<SectorRoutingRule>> ConfigureSectorRoutingAsync(
        User actor,
        ProductionSectorType sectorType,
        Guid primaryPrinterId,
        Guid? fallbackPrinterId = null,
        Guid? categoryId = null,
        int copiesCount = 1,
        bool autoPrintOnOrderAccepted = true,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<SectorRoutingRule>(guard.Error);

        var primary = await _printerRepo.FindByIdAsync(primaryPrinterId, cancellationToken);
        if (primary is null)
            return Result.Failure<SectorRoutingRule>(Error.NotFound("Printer.NotFound", "Impressora principal não encontrada."));
        TenantGuard.EnsureOwnership(_tenantContext, primary);

        if (fallbackPrinterId.HasValue && fallbackPrinterId.Value != Guid.Empty)
        {
            var fallback = await _printerRepo.FindByIdAsync(fallbackPrinterId.Value, cancellationToken);
            if (fallback is null)
                return Result.Failure<SectorRoutingRule>(Error.NotFound("Printer.BackupNotFound", "Impressora reserva não encontrada."));
            TenantGuard.EnsureOwnership(_tenantContext, fallback);
        }

        var existing = await _routingRepo.FindMatchingRuleAsync(sectorType, categoryId, cancellationToken);
        if (existing is not null && existing.CategoryId == categoryId)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existing);
            var update = existing.UpdatePrinters(primaryPrinterId, fallbackPrinterId, copiesCount);
            if (update.IsFailure)
                return Result.Failure<SectorRoutingRule>(update.Error);

            await _routingRepo.SaveChangesAsync(cancellationToken);
            return Result.Success(existing);
        }

        var res = SectorRoutingRule.Create(
            guard.Value,
            sectorType,
            primaryPrinterId,
            fallbackPrinterId,
            categoryId,
            copiesCount,
            autoPrintOnOrderAccepted,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _routingRepo.AddAsync(res.Value, cancellationToken);
        await _routingRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<PrintJob>> PrintSectorTicketAsync(
        User actor,
        ThermalTicketPayloadInput payload,
        Guid? categoryIdHint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var guard = ValidateActor(actor, PermissionAction.Print);
        if (guard.IsFailure)
            return Result.Failure<PrintJob>(guard.Error);

        var storeIdentityRes = await _storeIdentityProvider.GetStorePrintIdentityAsync(guard.Value, cancellationToken);
        if (storeIdentityRes.IsFailure)
            return Result.Failure<PrintJob>(storeIdentityRes.Error);

        var rule = await _routingRepo.FindMatchingRuleAsync(payload.SectorType, categoryIdHint, cancellationToken);
        if (rule is null)
            return Result.Failure<PrintJob>(Error.NotFound("Print.RoutingNotFound", $"Nenhuma impressora configurada para o setor {payload.SectorType}."));

        TenantGuard.EnsureOwnership(_tenantContext, rule);

        var primary = await _printerRepo.FindByIdAsync(rule.PrimaryPrinterId, cancellationToken);
        if (primary is null)
            return Result.Failure<PrintJob>(Error.NotFound("Printer.NotFound", "Impressora principal da regra não encontrada."));
        TenantGuard.EnsureOwnership(_tenantContext, primary);

        PrinterProfile? fallback = null;
        var backupId = rule.FallbackPrinterId ?? primary.BackupPrinterId;
        if (backupId.HasValue && backupId.Value != primary.Id)
        {
            fallback = await _printerRepo.FindByIdAsync(backupId.Value, cancellationToken);
            if (fallback is not null)
                TenantGuard.EnsureOwnership(_tenantContext, fallback);
        }

        var jobRes = PrintJob.Create(
            guard.Value,
            storeIdentityRes.Value,
            primary,
            payload with { CopiesCount = rule.CopiesCount },
            _clock.UtcNow);

        if (jobRes.IsFailure)
            return jobRes;

        var job = jobRes.Value;
        await DispatchJobWithFallbackAsync(job, primary, fallback, cancellationToken);

        await _jobRepo.AddAsync(job, cancellationToken);
        await _jobRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    public async Task<Result<IReadOnlyList<PrintJob>>> RouteOrderToSectorsAsync(
        User actor,
        RouteOrderTicketsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guard = ValidateActor(actor, PermissionAction.Print);
        if (guard.IsFailure)
            return Result.Failure<IReadOnlyList<PrintJob>>(guard.Error);

        if (request.Items.Count == 0)
            return Result.Failure<IReadOnlyList<PrintJob>>(Error.Validation("PrintJob.EmptyLines", "Pedido não possui itens para impressão."));

        var utcNow = _clock.UtcNow;
        var jobs = new List<PrintJob>();

        var sectorGroups = request.Items
            .GroupBy(i => (i.TargetSector, i.CategoryId))
            .ToList();

        foreach (var group in sectorGroups)
        {
            var payload = new ThermalTicketPayloadInput(
                group.Key.TargetSector,
                PrintDocumentKind.ProductionKitchenTicket,
                request.OrderOrTabReference,
                request.ChannelName,
                request.CustomerName,
                request.TableOrCallCode,
                request.StaffOperatorName,
                utcNow,
                group.Select(i => new TicketLineInput(i.ItemTitle, i.Quantity, i.CustomizationSummary, i.CustomerNotes, i.LineSubtotalAmount)).ToList());

            var res = await PrintSectorTicketAsync(actor, payload, group.Key.CategoryId, cancellationToken);
            if (res.IsFailure)
                return Result.Failure<IReadOnlyList<PrintJob>>(res.Error);

            jobs.Add(res.Value);
        }

        if (request.AlsoPrintCounterConference)
        {
            var counterPayload = new ThermalTicketPayloadInput(
                ProductionSectorType.Counter,
                PrintDocumentKind.CounterPickupConference,
                request.OrderOrTabReference,
                request.ChannelName,
                request.CustomerName,
                request.TableOrCallCode,
                request.StaffOperatorName,
                utcNow,
                request.Items.Select(i => new TicketLineInput(i.ItemTitle, i.Quantity, i.CustomizationSummary, i.CustomerNotes, i.LineSubtotalAmount)).ToList(),
                request.OrderTotalAmount);

            var counterRes = await PrintSectorTicketAsync(actor, counterPayload, null, cancellationToken);
            if (counterRes.IsFailure)
                return Result.Failure<IReadOnlyList<PrintJob>>(counterRes.Error);

            jobs.Add(counterRes.Value);
        }

        return Result.Success<IReadOnlyList<PrintJob>>(jobs);
    }

    public async Task<Result<PrintJob>> ReprintJobAsync(
        User actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Print);
        if (guard.IsFailure)
            return Result.Failure<PrintJob>(guard.Error);

        var job = await _jobRepo.FindByIdAsync(jobId, cancellationToken);
        if (job is null)
            return Result.Failure<PrintJob>(Error.NotFound("PrintJob.NotFound", "Trabalho de impressão não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, job);

        var primary = await _printerRepo.FindByIdAsync(job.TargetPrinterId, cancellationToken);
        if (primary is null)
            return Result.Failure<PrintJob>(Error.NotFound("Printer.NotFound", "Impressora alvo não encontrada."));
        TenantGuard.EnsureOwnership(_tenantContext, primary);

        PrinterProfile? fallback = null;
        var rule = await _routingRepo.FindMatchingRuleAsync(job.SectorType, null, cancellationToken);
        var backupId = rule?.FallbackPrinterId ?? primary.BackupPrinterId;
        if (backupId.HasValue && backupId.Value != primary.Id)
        {
            fallback = await _printerRepo.FindByIdAsync(backupId.Value, cancellationToken);
            if (fallback is not null)
                TenantGuard.EnsureOwnership(_tenantContext, fallback);
        }

        await DispatchJobWithFallbackAsync(job, primary, fallback, cancellationToken);
        await _jobRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    private async Task DispatchJobWithFallbackAsync(
        PrintJob job,
        PrinterProfile primary,
        PrinterProfile? fallback,
        CancellationToken cancellationToken)
    {
        var rawBytes = Convert.FromBase64String(job.EscPosPayloadBase64);
        var utcNow = _clock.UtcNow;

        string primaryFailureReason;
        if (primary.Status == PrinterStatus.Online)
        {
            var sendPrimary = await _printerTransport.SendEscPosAsync(primary, rawBytes, job.CopiesCount, cancellationToken);
            if (sendPrimary.IsSuccess)
            {
                job.MarkPrinted(primary.Id, usedFallback: false, fallbackReason: null, utcNow);
                return;
            }
            primaryFailureReason = $"{primary.PrinterCode}: {sendPrimary.Error.Message}";
        }
        else
        {
            primaryFailureReason = $"{primary.PrinterCode} em status {primary.Status}";
        }

        if (fallback is not null && fallback.Status == PrinterStatus.Online)
        {
            var sendBackup = await _printerTransport.SendEscPosAsync(fallback, rawBytes, job.CopiesCount, cancellationToken);
            if (sendBackup.IsSuccess)
            {
                job.MarkPrinted(fallback.Id, usedFallback: true, $"Fallback automático ({primaryFailureReason})", utcNow);
                return;
            }

            job.MarkFailed($"{primaryFailureReason} | Reserva {fallback.PrinterCode}: {sendBackup.Error.Message}");
            return;
        }

        var fallbackMsg = fallback is null
            ? "Sem impressora reserva configurada"
            : $"Reserva {fallback.PrinterCode} em status {fallback.Status}";

        job.MarkFailed($"{primaryFailureReason} | {fallbackMsg}");
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de operar impressão de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Printing, action))
            return Result.Failure<Guid>(Error.Forbidden("Printing.Forbidden", "Permissão insuficiente para operar impressoras."));

        return Result.Success(tenantId);
    }
}
