using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.PdvCashier.Application.Services;

public sealed record SettlePdvSaleRequest(
    Guid CashRegisterSessionId,
    decimal GrossAmount,
    IReadOnlyList<PdvPaymentAllocationInput> Payments,
    Guid? OrderId = null,
    decimal DiscountAmount = 0m,
    string? DiscountReason = null,
    User? DiscountApprover = null,
    string? IdempotencyKey = null);

public sealed record PdvSettlementResult(PdvSale Sale, PdvReceiptSnapshot Receipt);

public sealed record CashRegisterClosingReport(
    Guid CashRegisterSessionId,
    string TerminalCode,
    Money OpeningBalance,
    Money ExpectedCashBalance,
    Money CountedCashBalance,
    Money CashVariance,
    IReadOnlyDictionary<PaymentMethod, Money> NetTotalsByPaymentMethod);

/// <summary>
/// Caso de uso operacional de PDV e Caixa: abertura, sangria, suprimento, recebimentos múltiplos,
/// troco, descontos com aprovação gerencial, cancelamentos autorizados e conferência de fechamento (Seção 23).
/// </summary>
public sealed class PdvCashierService
{
    private readonly ICashRegisterRepository _registerRepo;
    private readonly IPdvSaleRepository _saleRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public PdvCashierService(
        ICashRegisterRepository registerRepo,
        IPdvSaleRepository saleRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _registerRepo = registerRepo;
        _saleRepo = saleRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<CashRegisterSession>> OpenSessionAsync(
        User actor,
        string terminalCode,
        decimal openingBalanceAmount,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<CashRegisterSession>(guard.Error);

        if (string.IsNullOrWhiteSpace(terminalCode))
            return Result.Failure<CashRegisterSession>(Error.Validation("CashRegister.TerminalRequired", "Código do terminal é obrigatório."));

        var normalizedTerminal = terminalCode.Trim().ToUpperInvariant();
        var alreadyOpen = await _registerRepo.FindOpenByTerminalAsync(normalizedTerminal, cancellationToken);
        if (alreadyOpen is not null)
            return Result.Failure<CashRegisterSession>(Error.Conflict("CashRegister.AlreadyOpen", $"Terminal '{normalizedTerminal}' já possui uma sessão aberta."));

        var moneyRes = Money.Create(openingBalanceAmount);
        if (moneyRes.IsFailure)
            return Result.Failure<CashRegisterSession>(moneyRes.Error);

        var sessionRes = CashRegisterSession.Open(guard.Value, normalizedTerminal, actor.Id, moneyRes.Value, _clock.UtcNow, storeUnitId);
        if (sessionRes.IsFailure)
            return sessionRes;

        await _registerRepo.AddAsync(sessionRes.Value, cancellationToken);
        await _registerRepo.SaveChangesAsync(cancellationToken);
        return sessionRes;
    }

    public async Task<Result<CashMovement>> RegisterCashSupplyAsync(
        User actor,
        Guid sessionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<CashMovement>(guard.Error);

        var session = await _registerRepo.FindByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Result.Failure<CashMovement>(Error.NotFound("CashRegister.NotFound", "Sessão de caixa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);

        var moneyRes = Money.Create(amount);
        if (moneyRes.IsFailure)
            return Result.Failure<CashMovement>(moneyRes.Error);

        var res = session.RegisterCashSupply(moneyRes.Value, reason, actor.Id, _clock.UtcNow);
        if (res.IsFailure)
            return res;

        await _registerRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<CashMovement>> RegisterCashWithdrawalAsync(
        User actor,
        Guid sessionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<CashMovement>(guard.Error);

        var session = await _registerRepo.FindByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Result.Failure<CashMovement>(Error.NotFound("CashRegister.NotFound", "Sessão de caixa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);

        var moneyRes = Money.Create(amount);
        if (moneyRes.IsFailure)
            return Result.Failure<CashMovement>(moneyRes.Error);

        var res = session.RegisterCashWithdrawal(moneyRes.Value, reason, actor.Id, _clock.UtcNow);
        if (res.IsFailure)
            return res;

        await _registerRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<PdvSettlementResult>> SettleSaleAsync(
        User actor,
        SettlePdvSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<PdvSettlementResult>(guard.Error);

        var normalizedKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (normalizedKey is not null)
        {
            var existingSale = await _saleRepo.FindByIdempotencyKeyAsync(normalizedKey, cancellationToken);
            if (existingSale is not null)
            {
                TenantGuard.EnsureOwnership(_tenantContext, existingSale);
                return Result.Success(new PdvSettlementResult(existingSale, existingSale.BuildReceiptSnapshot()));
            }
        }

        var session = await _registerRepo.FindByIdAsync(request.CashRegisterSessionId, cancellationToken);
        if (session is null)
            return Result.Failure<PdvSettlementResult>(Error.NotFound("CashRegister.NotFound", "Sessão de caixa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);

        var grossRes = Money.Create(request.GrossAmount);
        var discountRes = Money.Create(request.DiscountAmount);
        if (grossRes.IsFailure) return Result.Failure<PdvSettlementResult>(grossRes.Error);
        if (discountRes.IsFailure) return Result.Failure<PdvSettlementResult>(discountRes.Error);

        Guid? discountApproverId = null;
        if (discountRes.Value.Amount > 0m)
        {
            var approver = request.DiscountApprover ?? actor;
            if (approver.Scope == UserScope.Establishment && approver.TenantId != guard.Value)
                throw new CrossTenantAccessException(approver.TenantId, guard.Value, "Tentativa de autorizar desconto por usuário de outro estabelecimento.");

            if (!approver.HasPermission(SystemModules.Pdv, PermissionAction.Approve))
            {
                return Result.Failure<PdvSettlementResult>(Error.Forbidden(
                    "PdvSale.DiscountApprovalRequired",
                    "Desconto manual no PDV exige aprovação de gerente ou usuário com permissão de aprovação."));
            }

            discountApproverId = approver.Id;
        }

        var seq = await _saleRepo.CountAllAsync(cancellationToken) + 1;
        var receiptNumber = $"PDV-{seq:D6}";

        var saleRes = PdvSale.CreateAndSettle(
            session,
            request.OrderId,
            receiptNumber,
            grossRes.Value,
            discountRes.Value,
            request.DiscountReason,
            discountApproverId,
            request.Payments,
            actor.Id,
            _clock.UtcNow,
            normalizedKey);

        if (saleRes.IsFailure)
            return Result.Failure<PdvSettlementResult>(saleRes.Error);

        await _saleRepo.AddAsync(saleRes.Value, cancellationToken);
        await _saleRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new PdvSettlementResult(saleRes.Value, saleRes.Value.BuildReceiptSnapshot()));
    }

    public async Task<Result<PdvSale>> CancelSaleAsync(
        User actor,
        Guid pdvSaleId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Cancel);
        if (guard.IsFailure)
            return Result.Failure<PdvSale>(guard.Error);

        var sale = await _saleRepo.FindByIdAsync(pdvSaleId, cancellationToken);
        if (sale is null)
            return Result.Failure<PdvSale>(Error.NotFound("PdvSale.NotFound", "Venda PDV não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, sale);

        var session = await _registerRepo.FindByIdAsync(sale.CashRegisterSessionId, cancellationToken);
        if (session is null)
            return Result.Failure<PdvSale>(Error.NotFound("CashRegister.NotFound", "Sessão de caixa vinculada não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);

        var cancelRes = sale.CancelAndReverseInSession(session, actor.Id, reason, _clock.UtcNow);
        if (cancelRes.IsFailure)
            return Result.Failure<PdvSale>(cancelRes.Error);

        await _saleRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(sale);
    }

    public async Task<Result<CashRegisterClosingReport>> CloseSessionAsync(
        User actor,
        Guid sessionId,
        decimal countedCashAmount,
        string? closingNotes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<CashRegisterClosingReport>(guard.Error);

        var session = await _registerRepo.FindByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Result.Failure<CashRegisterClosingReport>(Error.NotFound("CashRegister.NotFound", "Sessão de caixa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, session);

        var countedRes = Money.Create(countedCashAmount);
        if (countedRes.IsFailure)
            return Result.Failure<CashRegisterClosingReport>(countedRes.Error);

        var closeRes = session.Close(countedRes.Value, actor.Id, closingNotes, _clock.UtcNow);
        if (closeRes.IsFailure)
            return Result.Failure<CashRegisterClosingReport>(closeRes.Error);

        await _registerRepo.SaveChangesAsync(cancellationToken);

        var totals = Enum.GetValues<PaymentMethod>()
            .ToDictionary(m => m, m => session.GetNetTotalByMethod(m));

        return Result.Success(new CashRegisterClosingReport(
            session.Id,
            session.TerminalCode,
            session.OpeningBalance,
            session.ExpectedCashBalance,
            session.CountedCashBalance!,
            session.CashVariance!,
            totals));
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar PDV/Caixa de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Pdv, action))
            return Result.Failure<Guid>(Error.Forbidden("Pdv.Forbidden", "Permissão insuficiente para operar o PDV e Caixa."));

        return Result.Success(currentTenantId);
    }
}
