using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.DiningHall.Application.Services;

public sealed record QrTableSessionSummary(
    QrTableSession Session,
    DiningTable Table,
    DiningTab OpenTab,
    SelfServiceStoreBranding StoreBranding);

/// <summary>
/// Serviço de autoatendimento por QR Code na mesa: abertura/retomada segura da comanda da mesa,
/// lançamento com recálculo autoritativo no servidor (Catálogo e Açaí), chamado de garçom e pedido de conta (Seção 25).
/// </summary>
public sealed class QrTableSelfServiceService
{
    private readonly IDiningTableRepository _tableRepo;
    private readonly IDiningTabRepository _tabRepo;
    private readonly IQrTableSessionRepository _sessionRepo;
    private readonly IWaiterCallRepository _waiterCallRepo;
    private readonly ICatalogItemPricingService _catalogPricing;
    private readonly IAcaiAssemblyPricingService _acaiPricing;
    private readonly ISelfServiceBrandingProvider _brandingProvider;
    private readonly IWaiterCallRealtimeNotifier _waiterCallNotifier;
    private readonly ISelfServiceOrderDispatcher _orderDispatcher;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public QrTableSelfServiceService(
        IDiningTableRepository tableRepo,
        IDiningTabRepository tabRepo,
        IQrTableSessionRepository sessionRepo,
        IWaiterCallRepository waiterCallRepo,
        ICatalogItemPricingService catalogPricing,
        IAcaiAssemblyPricingService acaiPricing,
        ISelfServiceBrandingProvider brandingProvider,
        IWaiterCallRealtimeNotifier waiterCallNotifier,
        ISelfServiceOrderDispatcher orderDispatcher,
        ITenantContext tenantContext,
        IClock clock)
    {
        _tableRepo = tableRepo;
        _tabRepo = tabRepo;
        _sessionRepo = sessionRepo;
        _waiterCallRepo = waiterCallRepo;
        _catalogPricing = catalogPricing;
        _acaiPricing = acaiPricing;
        _brandingProvider = brandingProvider;
        _waiterCallNotifier = waiterCallNotifier;
        _orderDispatcher = orderDispatcher;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<QrTableSessionSummary>> StartOrResumeByQrTokenAsync(
        string qrCodeToken,
        string? customerName = null,
        decimal defaultServiceChargePercent = 10m,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(qrCodeToken))
            return Result.Failure<QrTableSessionSummary>(Error.Validation("QrSession.TokenRequired", "Token de QR Code da mesa é obrigatório."));

        var table = await _tableRepo.FindByQrTokenAsync(qrCodeToken.Trim(), cancellationToken);
        if (table is null)
            return Result.Failure<QrTableSessionSummary>(Error.NotFound("Dining.TableNotFound", "Mesa não encontrada para o QR Code informado."));

        TenantGuard.EnsureOwnership(_tenantContext, table);
        if (table.Status == DiningTableStatus.Inactive)
            return Result.Failure<QrTableSessionSummary>(Error.Conflict("Dining.TableInactive", "Mesa desativada no momento."));

        var utcNow = _clock.UtcNow;
        DiningTab? openTab = null;
        if (table.CurrentTabId.HasValue)
        {
            var existingTab = await _tabRepo.FindByIdAsync(table.CurrentTabId.Value, cancellationToken);
            if (existingTab is not null)
            {
                TenantGuard.EnsureOwnership(_tenantContext, existingTab);
                if (existingTab.Status is not (DiningTabStatus.Closed or DiningTabStatus.Cancelled))
                    openTab = existingTab;
            }
        }

        if (openTab is null)
        {
            var seq = await _tabRepo.CountAllAsync(cancellationToken) + 1;
            var tabCode = $"COM-{seq:D4}";
            var resolvedCustomer = string.IsNullOrWhiteSpace(customerName)
                ? $"Mesa {table.TableCode} (QR)"
                : customerName.Trim();

            var openRes = DiningTab.OpenSelfService(
                tenantId,
                tabCode,
                table,
                resolvedCustomer,
                guestCount: 1,
                serviceChargePercent: defaultServiceChargePercent,
                openedAtUtc: utcNow);

            if (openRes.IsFailure)
                return Result.Failure<QrTableSessionSummary>(openRes.Error);

            openTab = openRes.Value;
            await _tabRepo.AddAsync(openTab, cancellationToken);
        }

        var existingSession = await _sessionRepo.FindActiveByTableAsync(table.Id, cancellationToken);
        if (existingSession is not null && !existingSession.IsExpired(utcNow) && existingSession.DiningTabId == openTab.Id)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existingSession);
            existingSession.EnsureOperational(utcNow);
            await _sessionRepo.SaveChangesAsync(cancellationToken);

            var existingBranding = await ResolveStoreOnlyBrandingAsync(tenantId, cancellationToken);
            return Result.Success(new QrTableSessionSummary(existingSession, table, openTab, existingBranding));
        }

        var sessionRes = QrTableSession.Create(
            tenantId,
            table,
            openTab.Id,
            customerName ?? openTab.CustomerName,
            utcNow);

        if (sessionRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(sessionRes.Error);

        await _sessionRepo.AddAsync(sessionRes.Value, cancellationToken);
        await _sessionRepo.SaveChangesAsync(cancellationToken);

        var branding = await ResolveStoreOnlyBrandingAsync(tenantId, cancellationToken);
        return Result.Success(new QrTableSessionSummary(sessionRes.Value, table, openTab, branding));
    }

    public async Task<Result<QrTableSessionSummary>> AddCatalogProductAsync(
        string sessionToken,
        Guid productId,
        Guid? variationId = null,
        IReadOnlyList<CatalogModifierSelection>? modifiers = null,
        int quantity = 1,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadActiveSessionContextAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(loadRes.Error);

        var (session, table, tab) = loadRes.Value;
        var quoteRes = await _catalogPricing.QuoteProductItemAsync(productId, variationId, modifiers, cancellationToken);
        if (quoteRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(quoteRes.Error);

        var quote = quoteRes.Value;
        var summary = string.IsNullOrWhiteSpace(customerNotes)
            ? quote.CustomizationSummaryText
            : $"{quote.CustomizationSummaryText} | Obs: {customerNotes.Trim()}";

        var utcNow = _clock.UtcNow;
        var addRes = tab.AddItem(
            quote.ProductId,
            quote.ProductName,
            summary,
            quote.UnitPrice,
            quantity,
            Guid.Empty,
            utcNow);

        if (addRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(addRes.Error);

        var dispatchRes = await _orderDispatcher.DispatchAsync(new SelfServiceOrderDispatchRequest(
            session.TenantId,
            "QrCodeTable",
            "DineIn",
            session.CustomerName,
            $"Mesa {table.TableCode}",
            tab.TabCode,
            null,
            [new SelfServiceDispatchedLine(quote.ProductId, null, quote.ProductName, summary, quote.UnitPrice, quantity, customerNotes)],
            addRes.Value.LineSubtotal), cancellationToken);

        if (dispatchRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(dispatchRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        var branding = await ResolveStoreOnlyBrandingAsync(session.TenantId, cancellationToken);
        return Result.Success(new QrTableSessionSummary(session, table, tab, branding));
    }

    public async Task<Result<QrTableSessionSummary>> AddCustomAcaiAsync(
        string sessionToken,
        Guid cupSizeId,
        IReadOnlyList<Guid> baseFlavorIds,
        IReadOnlyList<AcaiItemSelection>? freeToppings = null,
        IReadOnlyList<AcaiItemSelection>? paidAddons = null,
        int quantity = 1,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadActiveSessionContextAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(loadRes.Error);

        var (session, table, tab) = loadRes.Value;
        var acaiRes = await _acaiPricing.BuildAndPriceCupAsync(
            new AcaiCupBuildRequest(cupSizeId, baseFlavorIds ?? [], freeToppings ?? [], paidAddons ?? [], 1, customerNotes),
            cancellationToken);

        if (acaiRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(acaiRes.Error);

        var cup = acaiRes.Value;
        var utcNow = _clock.UtcNow;
        var (title, summaryText) = SelfServiceFormattingHelper.FormatCustomAcai(cup);

        var addRes = tab.AddItem(
            null,
            title,
            summaryText,
            cup.UnitFinalPrice,
            quantity,
            Guid.Empty,
            utcNow);

        if (addRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(addRes.Error);

        var dispatchRes = await _orderDispatcher.DispatchAsync(new SelfServiceOrderDispatchRequest(
            session.TenantId,
            "QrCodeTable",
            "DineIn",
            session.CustomerName,
            $"Mesa {table.TableCode}",
            tab.TabCode,
            null,
            [new SelfServiceDispatchedLine(null, cupSizeId, title, summaryText, cup.UnitFinalPrice, quantity, customerNotes)],
            addRes.Value.LineSubtotal), cancellationToken);

        if (dispatchRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(dispatchRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        var branding = await ResolveStoreOnlyBrandingAsync(session.TenantId, cancellationToken);
        return Result.Success(new QrTableSessionSummary(session, table, tab, branding));
    }

    public async Task<Result<WaiterCall>> CallWaiterAsync(
        string sessionToken,
        WaiterCallType callType,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadActiveSessionContextAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<WaiterCall>(loadRes.Error);

        var (session, table, tab) = loadRes.Value;
        var utcNow = _clock.UtcNow;

        if (callType == WaiterCallType.RequestBill)
        {
            var reqClose = tab.RequestClosing(null, table, utcNow);
            if (reqClose.IsFailure)
                return Result.Failure<WaiterCall>(reqClose.Error);

            session.RequestBill(utcNow);
        }

        var callRes = WaiterCall.Create(
            session.TenantId,
            table,
            callType,
            tab.Id,
            session.Id,
            customerNotes,
            utcNow);

        if (callRes.IsFailure)
            return callRes;

        await _waiterCallRepo.AddAsync(callRes.Value, cancellationToken);
        await _waiterCallRepo.SaveChangesAsync(cancellationToken);

        await _waiterCallNotifier.NotifyAsync(new WaiterCallRealtimeEvent(
            callRes.Value.TenantId,
            callRes.Value.Id,
            callRes.Value.DiningTableId,
            callRes.Value.TableCode,
            callRes.Value.CallType,
            callRes.Value.Status,
            callRes.Value.CustomerNotes,
            utcNow), cancellationToken);

        return callRes;
    }

    public async Task<Result<QrTableSessionSummary>> GetSummaryAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadActiveSessionContextAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<QrTableSessionSummary>(loadRes.Error);

        var (session, table, tab) = loadRes.Value;
        var branding = await ResolveStoreOnlyBrandingAsync(session.TenantId, cancellationToken);
        return Result.Success(new QrTableSessionSummary(session, table, tab, branding));
    }

    public async Task<Result<WaiterCall>> AcknowledgeWaiterCallAsync(
        User staffActor,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateStaffActor(staffActor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<WaiterCall>(guard.Error);

        var call = await _waiterCallRepo.FindByIdAsync(callId, cancellationToken);
        if (call is null)
            return Result.Failure<WaiterCall>(Error.NotFound("WaiterCall.NotFound", "Chamado não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, call);
        var ack = call.Acknowledge(staffActor.Id, _clock.UtcNow);
        if (ack.IsFailure)
            return Result.Failure<WaiterCall>(ack.Error);

        await _waiterCallRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(call);
    }

    public async Task<Result<WaiterCall>> CompleteWaiterCallAsync(
        User staffActor,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateStaffActor(staffActor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<WaiterCall>(guard.Error);

        var call = await _waiterCallRepo.FindByIdAsync(callId, cancellationToken);
        if (call is null)
            return Result.Failure<WaiterCall>(Error.NotFound("WaiterCall.NotFound", "Chamado não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, call);
        var comp = call.Complete(staffActor.Id, _clock.UtcNow);
        if (comp.IsFailure)
            return Result.Failure<WaiterCall>(comp.Error);

        await _waiterCallRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(call);
    }

    private async Task<Result<(QrTableSession Session, DiningTable Table, DiningTab Tab)>> LoadActiveSessionContextAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return Result.Failure<(QrTableSession, DiningTable, DiningTab)>(Error.Validation("QrSession.TokenRequired", "Sessão QR Code é obrigatória."));

        var session = await _sessionRepo.FindBySessionTokenAsync(sessionToken.Trim(), cancellationToken);
        if (session is null)
            return Result.Failure<(QrTableSession, DiningTable, DiningTab)>(Error.NotFound("QrSession.NotFound", "Sessão QR Code não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);
        var check = session.EnsureOperational(_clock.UtcNow);
        if (check.IsFailure)
        {
            await _sessionRepo.SaveChangesAsync(cancellationToken);
            return Result.Failure<(QrTableSession, DiningTable, DiningTab)>(check.Error);
        }

        var table = await _tableRepo.FindByIdAsync(session.DiningTableId, cancellationToken);
        if (table is null)
            return Result.Failure<(QrTableSession, DiningTable, DiningTab)>(Error.NotFound("Dining.TableNotFound", "Mesa vinculada não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, table);

        var tab = await _tabRepo.FindByIdAsync(session.DiningTabId, cancellationToken);
        if (tab is null)
            return Result.Failure<(QrTableSession, DiningTable, DiningTab)>(Error.NotFound("Tab.NotFound", "Comanda da mesa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);
        return Result.Success((session, table, tab));
    }

    private Task<SelfServiceStoreBranding> ResolveStoreOnlyBrandingAsync(Guid tenantId, CancellationToken cancellationToken) =>
        SelfServiceFormattingHelper.ResolveStoreOnlyBrandingAsync(_brandingProvider, tenantId, cancellationToken);

    private Result<Guid> ValidateStaffActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de atender chamado de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.DiningHall, action))
            return Result.Failure<Guid>(Error.Forbidden("Dining.Forbidden", "Permissão insuficiente para atender chamados de salão."));

        return Result.Success(tenantId);
    }
}
