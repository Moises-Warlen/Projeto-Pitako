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

public sealed record TotemStartResult(
    TotemOrderSession Session,
    TotemTerminal Terminal,
    SelfServiceStoreBranding StoreBranding);

public sealed record TotemConfirmationReceipt(
    TotemOrderSession Session,
    string PickupCallCode,
    string DispatchedOrderNumber,
    SelfServiceStoreBranding StoreBranding);

/// <summary>
/// Serviço de Totem de Autoatendimento: cadastro de terminais, jornada com timeout de inatividade,
/// escolha comer no local ou retirar, cálculo autoritativo no servidor, idempotência e emissão de senha (Seção 25).
/// </summary>
public sealed class TotemSelfServiceService
{
    private readonly ITotemTerminalRepository _terminalRepo;
    private readonly ITotemOrderSessionRepository _sessionRepo;
    private readonly ICatalogItemPricingService _catalogPricing;
    private readonly IAcaiAssemblyPricingService _acaiPricing;
    private readonly ISelfServiceBrandingProvider _brandingProvider;
    private readonly ISelfServiceOrderDispatcher _orderDispatcher;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public TotemSelfServiceService(
        ITotemTerminalRepository terminalRepo,
        ITotemOrderSessionRepository sessionRepo,
        ICatalogItemPricingService catalogPricing,
        IAcaiAssemblyPricingService acaiPricing,
        ISelfServiceBrandingProvider brandingProvider,
        ISelfServiceOrderDispatcher orderDispatcher,
        ITenantContext tenantContext,
        IClock clock)
    {
        _terminalRepo = terminalRepo;
        _sessionRepo = sessionRepo;
        _catalogPricing = catalogPricing;
        _acaiPricing = acaiPricing;
        _brandingProvider = brandingProvider;
        _orderDispatcher = orderDispatcher;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<TotemTerminal>> RegisterTerminalAsync(
        User adminActor,
        string terminalCode,
        string displayName,
        TotemFulfillmentChoice defaultChoice = TotemFulfillmentChoice.CounterPickup,
        bool requireCustomerName = true,
        bool requireTableOrPagerNumber = false,
        int idleTimeoutSeconds = 120,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateAdminActor(adminActor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<TotemTerminal>(guard.Error);

        var existing = await _terminalRepo.FindByCodeAsync(terminalCode.Trim().ToUpperInvariant(), cancellationToken);
        if (existing is not null)
            return Result.Failure<TotemTerminal>(Error.Conflict("Totem.DuplicateCode", $"Totem '{terminalCode}' já cadastrado."));

        var res = TotemTerminal.Create(
            guard.Value,
            terminalCode,
            displayName,
            defaultChoice,
            requireCustomerName,
            requireTableOrPagerNumber,
            idleTimeoutSeconds,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _terminalRepo.AddAsync(res.Value, cancellationToken);
        await _terminalRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<TotemTerminal>> UpdateTerminalStatusAsync(
        User adminActor,
        Guid terminalId,
        TotemTerminalStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateAdminActor(adminActor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<TotemTerminal>(guard.Error);

        var terminal = await _terminalRepo.FindByIdAsync(terminalId, cancellationToken);
        if (terminal is null)
            return Result.Failure<TotemTerminal>(Error.NotFound("Totem.NotFound", "Terminal de totem não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, terminal);
        terminal.SetStatus(newStatus);
        await _terminalRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(terminal);
    }

    public async Task<Result<TotemStartResult>> StartSessionAsync(
        string terminalCode,
        TotemFulfillmentChoice? choice = null,
        string? customerName = null,
        string? pagerOrTableTag = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(terminalCode))
            return Result.Failure<TotemStartResult>(Error.Validation("Totem.CodeRequired", "Código do totem é obrigatório."));

        var terminal = await _terminalRepo.FindByCodeAsync(terminalCode.Trim().ToUpperInvariant(), cancellationToken);
        if (terminal is null)
            return Result.Failure<TotemStartResult>(Error.NotFound("Totem.NotFound", "Terminal de totem não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, terminal);

        var utcNow = _clock.UtcNow;
        terminal.RecordHeartbeat(utcNow);

        var startRes = TotemOrderSession.Start(tenantId, terminal, choice, customerName, pagerOrTableTag, utcNow);
        if (startRes.IsFailure)
            return Result.Failure<TotemStartResult>(startRes.Error);

        await _sessionRepo.AddAsync(startRes.Value, cancellationToken);
        await _sessionRepo.SaveChangesAsync(cancellationToken);

        var branding = await ResolveStoreOnlyBrandingAsync(tenantId, cancellationToken);
        return Result.Success(new TotemStartResult(startRes.Value, terminal, branding));
    }

    public async Task<Result<TotemOrderSession>> AddCatalogProductAsync(
        string sessionToken,
        Guid productId,
        Guid? variationId = null,
        IReadOnlyList<CatalogModifierSelection>? modifiers = null,
        int quantity = 1,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadSessionAndTerminalAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<TotemOrderSession>(loadRes.Error);

        var (session, terminal) = loadRes.Value;
        var quoteRes = await _catalogPricing.QuoteProductItemAsync(productId, variationId, modifiers, cancellationToken);
        if (quoteRes.IsFailure)
            return Result.Failure<TotemOrderSession>(quoteRes.Error);

        var quote = quoteRes.Value;
        var addRes = session.AddLine(
            quote.ProductId,
            null,
            quote.ProductName,
            quote.CustomizationSummaryText,
            quote.CanonicalSelectionsJson,
            quote.UnitPrice,
            quantity,
            customerNotes,
            terminal.IdleTimeoutSeconds,
            _clock.UtcNow);

        await _sessionRepo.SaveChangesAsync(cancellationToken);
        return addRes.IsFailure ? Result.Failure<TotemOrderSession>(addRes.Error) : Result.Success(session);
    }

    public async Task<Result<TotemOrderSession>> AddCustomAcaiAsync(
        string sessionToken,
        Guid cupSizeId,
        IReadOnlyList<Guid> baseFlavorIds,
        IReadOnlyList<AcaiItemSelection>? freeToppings = null,
        IReadOnlyList<AcaiItemSelection>? paidAddons = null,
        int quantity = 1,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadSessionAndTerminalAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<TotemOrderSession>(loadRes.Error);

        var (session, terminal) = loadRes.Value;
        var acaiRes = await _acaiPricing.BuildAndPriceCupAsync(
            new AcaiCupBuildRequest(cupSizeId, baseFlavorIds ?? [], freeToppings ?? [], paidAddons ?? [], 1, customerNotes),
            cancellationToken);

        if (acaiRes.IsFailure)
            return Result.Failure<TotemOrderSession>(acaiRes.Error);

        var cup = acaiRes.Value;
        var (title, summaryText) = SelfServiceFormattingHelper.FormatCustomAcai(cup);

        var canonicalAcai = System.Text.Json.JsonSerializer.Serialize(new
        {
            cup.CupSizeId,
            BaseFlavors = cup.BaseFlavors.OrderBy(f => f.FlavorId).Select(f => f.FlavorId),
            Free = cup.FreeToppings.OrderBy(t => t.ToppingId).Select(t => new { t.ToppingId, t.Quantity }),
            Paid = cup.PaidAddons.OrderBy(a => a.AddonId).Select(a => new { a.AddonId, a.Quantity })
        });

        var addRes = session.AddLine(
            null,
            cupSizeId,
            title,
            summaryText,
            canonicalAcai,
            cup.UnitFinalPrice,
            quantity,
            customerNotes,
            terminal.IdleTimeoutSeconds,
            _clock.UtcNow);

        await _sessionRepo.SaveChangesAsync(cancellationToken);
        return addRes.IsFailure ? Result.Failure<TotemOrderSession>(addRes.Error) : Result.Success(session);
    }

    public async Task<Result<TotemOrderSession>> RemoveLineAsync(
        string sessionToken,
        Guid lineId,
        CancellationToken cancellationToken = default)
    {
        var loadRes = await LoadSessionAndTerminalAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<TotemOrderSession>(loadRes.Error);

        var (session, terminal) = loadRes.Value;
        var remRes = session.RemoveLine(lineId, terminal.IdleTimeoutSeconds, _clock.UtcNow);
        await _sessionRepo.SaveChangesAsync(cancellationToken);
        return remRes.IsFailure ? Result.Failure<TotemOrderSession>(remRes.Error) : Result.Success(session);
    }

    public async Task<Result<TotemConfirmationReceipt>> ConfirmCheckoutAsync(
        string sessionToken,
        TotemPaymentChoice paymentChoice,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existingIdempotent = await _sessionRepo.FindByIdempotencyKeyAsync(normalizedKey, cancellationToken);
            if (existingIdempotent is not null && existingIdempotent.Status == TotemSessionStatus.Confirmed)
            {
                TenantGuard.EnsureOwnership(_tenantContext, existingIdempotent);
                var existingBrand = await ResolveStoreOnlyBrandingAsync(existingIdempotent.TenantId, cancellationToken);
                return Result.Success(new TotemConfirmationReceipt(
                    existingIdempotent,
                    existingIdempotent.PickupCallCode ?? string.Empty,
                    existingIdempotent.DispatchedOrderNumber ?? string.Empty,
                    existingBrand));
            }
        }

        var loadRes = await LoadSessionAndTerminalAsync(sessionToken, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<TotemConfirmationReceipt>(loadRes.Error);

        var (session, terminal) = loadRes.Value;
        if (session.Status == TotemSessionStatus.Confirmed)
        {
            var confirmedBrand = await ResolveStoreOnlyBrandingAsync(session.TenantId, cancellationToken);
            return Result.Success(new TotemConfirmationReceipt(
                session,
                session.PickupCallCode ?? string.Empty,
                session.DispatchedOrderNumber ?? string.Empty,
                confirmedBrand));
        }

        var utcNow = _clock.UtcNow;
        if (session.Lines.Count == 0)
            return Result.Failure<TotemConfirmationReceipt>(Error.Validation("Totem.EmptyBasket", "Adicione ao menos um item antes de confirmar no totem."));

        var nextSeq = await _sessionRepo.CountConfirmedAsync(cancellationToken) + 1;
        var pickupCode = $"T-{nextSeq:D4}";
        var fulfillmentCode = session.FulfillmentChoice == TotemFulfillmentChoice.DineIn ? "DineIn" : "CounterPickup";
        var dispatchedLines = session.Lines
            .Select(l => new SelfServiceDispatchedLine(
                l.ProductId,
                l.AcaiCupSizeId,
                l.ItemTitle,
                l.CustomizationSummary,
                l.UnitPrice,
                l.Quantity,
                l.CustomerNotes))
            .ToList();

        var dispatchRes = await _orderDispatcher.DispatchAsync(new SelfServiceOrderDispatchRequest(
            session.TenantId,
            "SelfServiceTotem",
            fulfillmentCode,
            session.CustomerName ?? $"Totem {pickupCode}",
            session.PagerOrTableTag ?? terminal.TerminalCode,
            pickupCode,
            terminal.StoreUnitId,
            dispatchedLines,
            session.ItemsSubtotal), cancellationToken);

        if (dispatchRes.IsFailure)
            return Result.Failure<TotemConfirmationReceipt>(dispatchRes.Error);

        var confirmRes = session.Confirm(
            paymentChoice,
            pickupCode,
            dispatchRes.Value.OrderNumber,
            normalizedKey,
            utcNow);

        await _sessionRepo.SaveChangesAsync(cancellationToken);
        if (confirmRes.IsFailure)
            return Result.Failure<TotemConfirmationReceipt>(confirmRes.Error);

        var branding = await ResolveStoreOnlyBrandingAsync(session.TenantId, cancellationToken);
        return Result.Success(new TotemConfirmationReceipt(
            session,
            pickupCode,
            dispatchRes.Value.OrderNumber,
            branding));
    }

    private async Task<Result<(TotemOrderSession Session, TotemTerminal Terminal)>> LoadSessionAndTerminalAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return Result.Failure<(TotemOrderSession, TotemTerminal)>(Error.Validation("Totem.SessionRequired", "Sessão do totem é obrigatória."));

        var session = await _sessionRepo.FindBySessionTokenAsync(sessionToken.Trim(), cancellationToken);
        if (session is null)
            return Result.Failure<(TotemOrderSession, TotemTerminal)>(Error.NotFound("Totem.SessionNotFound", "Sessão do totem não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);
        var terminal = await _terminalRepo.FindByIdAsync(session.TotemTerminalId, cancellationToken);
        if (terminal is null)
            return Result.Failure<(TotemOrderSession, TotemTerminal)>(Error.NotFound("Totem.TerminalNotFound", "Terminal de totem não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, terminal);
        return Result.Success((session, terminal));
    }

    private Task<SelfServiceStoreBranding> ResolveStoreOnlyBrandingAsync(Guid tenantId, CancellationToken cancellationToken) =>
        SelfServiceFormattingHelper.ResolveStoreOnlyBrandingAsync(_brandingProvider, tenantId, cancellationToken);

    private Result<Guid> ValidateAdminActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de configurar totem de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.DiningHall, action))
            return Result.Failure<Guid>(Error.Forbidden("Dining.Forbidden", "Permissão gerencial/administrativa necessária para configurar totens."));

        return Result.Success(tenantId);
    }
}
