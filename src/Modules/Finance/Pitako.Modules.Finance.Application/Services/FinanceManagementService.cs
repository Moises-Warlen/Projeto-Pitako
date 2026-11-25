using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Finance.Application.Services;

/// <summary>
/// Serviço Financeiro completo: Contas a Pagar/Receber, Fluxo de Caixa realizado/projetado,
/// DRE Gerencial, Conciliação de extratos e Gateways de pagamento tokenizados PCI-safe (Seção 33).
/// </summary>
public sealed class FinanceManagementService
{
    private readonly IFinancialEntryRepository _entryRepo;
    private readonly ICashFlowLedgerRepository _cashFlowRepo;
    private readonly IPaymentGatewayConfigRepository _gatewayConfigRepo;
    private readonly IPaymentTransactionRepository _txRepo;
    private readonly IReconciliationBatchRepository _reconRepo;
    private readonly IFinanceSecretProtector _secretProtector;
    private readonly IPaymentGatewayClient _gatewayClient;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public FinanceManagementService(
        IFinancialEntryRepository entryRepo,
        ICashFlowLedgerRepository cashFlowRepo,
        IPaymentGatewayConfigRepository gatewayConfigRepo,
        IPaymentTransactionRepository txRepo,
        IReconciliationBatchRepository reconRepo,
        IFinanceSecretProtector secretProtector,
        IPaymentGatewayClient gatewayClient,
        ITenantContext tenantContext,
        IClock clock)
    {
        _entryRepo = entryRepo;
        _cashFlowRepo = cashFlowRepo;
        _gatewayConfigRepo = gatewayConfigRepo;
        _txRepo = txRepo;
        _reconRepo = reconRepo;
        _secretProtector = secretProtector;
        _gatewayClient = gatewayClient;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<FinancialEntry>> CreateEntryAsync(
        User actor,
        FinancialEntryKind kind,
        DreLineCategory dreCategory,
        string counterpartyName,
        string description,
        decimal originalAmount,
        DateTimeOffset dueDateUtc,
        string? referenceDocument = null,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<FinancialEntry>(guard.Error);

        var seq = await _entryRepo.CountAllAsync(cancellationToken) + 1;
        var prefix = kind == FinancialEntryKind.Receivable ? "CR" : "CP";
        var code = $"{prefix}-{seq:D5}";

        var res = FinancialEntry.Create(
            guard.Value,
            code,
            kind,
            dreCategory,
            counterpartyName,
            description,
            originalAmount,
            dueDateUtc,
            referenceDocument,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _entryRepo.AddAsync(res.Value, cancellationToken);
        await _entryRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<FinancialEntry>> SettleEntryAsync(
        User actor,
        Guid entryId,
        decimal netPaidAmount,
        decimal feeOrAdjustmentAmount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<FinancialEntry>(guard.Error);

        var entry = await _entryRepo.FindByIdAsync(entryId, cancellationToken);
        if (entry is null)
            return Result.Failure<FinancialEntry>(Error.NotFound("Finance.EntryNotFound", "Título financeiro não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, entry);

        var paidMoney = Money.Create(netPaidAmount);
        var feeMoney = Money.Create(Math.Max(0m, feeOrAdjustmentAmount));
        if (paidMoney.IsFailure || feeMoney.IsFailure)
            return Result.Failure<FinancialEntry>(paidMoney.IsFailure ? paidMoney.Error : feeMoney.Error);

        var utcNow = _clock.UtcNow;
        var settleRes = entry.RecordSettlement(paidMoney.Value, feeMoney.Value, paymentMethod, utcNow);
        if (settleRes.IsFailure)
            return Result.Failure<FinancialEntry>(settleRes.Error);

        var direction = entry.Kind == FinancialEntryKind.Receivable
            ? CashFlowDirection.Inflow
            : CashFlowDirection.Outflow;

        var ledgerRes = CashFlowLedgerEntry.Record(
            guard.Value,
            direction,
            $"Liquidação {entry.EntryCode} ({entry.CounterpartyName})",
            paidMoney.Value,
            paymentMethod,
            utcNow,
            entry.Id,
            entry.StoreUnitId);

        if (ledgerRes.IsFailure)
            return Result.Failure<FinancialEntry>(ledgerRes.Error);

        await _cashFlowRepo.AddAsync(ledgerRes.Value, cancellationToken);
        await _entryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    public async Task<Result<PaymentGatewayConfig>> ConfigurePaymentGatewayAsync(
        User actor,
        PaymentGatewayProvider provider,
        string merchantAccountCode,
        string rawApiSecret,
        decimal defaultMdrPercent,
        int settlementDays = 1,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<PaymentGatewayConfig>(guard.Error);

        if (string.IsNullOrWhiteSpace(rawApiSecret))
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.GatewaySecretRequired", "Chave secreta do gateway é obrigatória."));

        var encrypted = _secretProtector.Protect(rawApiSecret.Trim());
        var res = PaymentGatewayConfig.Create(
            guard.Value,
            provider,
            merchantAccountCode,
            encrypted,
            defaultMdrPercent,
            settlementDays);

        if (res.IsFailure)
            return res;

        await _gatewayConfigRepo.AddAsync(res.Value, cancellationToken);
        await _gatewayConfigRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<PaymentTransactionRecord>> ProcessGatewayChargeAsync(
        User actor,
        Guid gatewayConfigId,
        ExternalGatewayChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<PaymentTransactionRecord>(guard.Error);

        var pciGuard = PciCardDataGuard.EnsureNoRawCardData(
            request.OrderOrSaleReference,
            request.CardBrandOrPix,
            request.CardLast4Digits,
            request.GatewayTokenReference);
        if (pciGuard.IsFailure)
            return Result.Failure<PaymentTransactionRecord>(pciGuard.Error);

        var last4Res = PciCardDataGuard.ValidateLast4Digits(request.CardLast4Digits);
        if (last4Res.IsFailure)
            return Result.Failure<PaymentTransactionRecord>(last4Res.Error);

        var config = await _gatewayConfigRepo.FindByIdAsync(gatewayConfigId, cancellationToken);
        if (config is null)
            return Result.Failure<PaymentTransactionRecord>(Error.NotFound("Finance.GatewayNotFound", "Configuração de gateway não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, config);

        var utcNow = _clock.UtcNow;
        var decrypted = _secretProtector.Unprotect(config.EncryptedSecretBlob);
        var chargeRes = await _gatewayClient.ProcessTokenizedChargeAsync(config, decrypted, request, cancellationToken);

        if (chargeRes.IsFailure)
        {
            var failedRec = PaymentTransactionRecord.Create(
                guard.Value,
                config.Id,
                config.Provider,
                request.OrderOrSaleReference,
                $"FAILED-{Guid.NewGuid():N}"[..20],
                null,
                request.CardBrandOrPix,
                last4Res.Value,
                request.GrossAmount,
                0m,
                PaymentTransactionStatus.Failed,
                chargeRes.Error.Message,
                utcNow);

            if (failedRec.IsFailure)
                return failedRec;

            await _txRepo.AddAsync(failedRec.Value, cancellationToken);
            await _txRepo.SaveChangesAsync(cancellationToken);
            return Result.Failure<PaymentTransactionRecord>(chargeRes.Error);
        }

        var ok = chargeRes.Value;
        var recRes = PaymentTransactionRecord.Create(
            guard.Value,
            config.Id,
            config.Provider,
            request.OrderOrSaleReference,
            ok.ExternalTransactionId,
            ok.NsuCode,
            request.CardBrandOrPix,
            last4Res.Value,
            request.GrossAmount,
            ok.FeeAmount,
            PaymentTransactionStatus.Captured,
            null,
            utcNow);

        if (recRes.IsFailure)
            return recRes;

        await _txRepo.AddAsync(recRes.Value, cancellationToken);
        await _txRepo.SaveChangesAsync(cancellationToken);
        return recRes;
    }

    public async Task<Result<SettlementReconciliationBatch>> RunSettlementReconciliationAsync(
        User actor,
        string providerName,
        IReadOnlyList<StatementSettlementRow> statementRows,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<SettlementReconciliationBatch>(guard.Error);

        if (statementRows is null || statementRows.Count == 0)
            return Result.Failure<SettlementReconciliationBatch>(Error.Validation("Finance.EmptyStatement", "Extrato para conciliação vazio."));

        var refs = statementRows.Select(r => r.ExternalTransactionRef.Trim()).ToList();
        var internalTxs = (await _txRepo.FindByExternalRefsAsync(refs, cancellationToken))
            .ToDictionary(t => t.ExternalTransactionId, StringComparer.OrdinalIgnoreCase);

        var seq = await _reconRepo.CountAllAsync(cancellationToken) + 1;
        var batch = SettlementReconciliationBatch.Start(guard.Value, $"CONC-{seq:D4}", providerName, _clock.UtcNow);

        foreach (var row in statementRows)
        {
            var found = internalTxs.TryGetValue(row.ExternalTransactionRef.Trim(), out var tx);
            batch.AddLine(row.ExternalTransactionRef, found ? tx!.NetAmount.Amount : 0m, row.StatementNetAmount, found);
        }

        await _reconRepo.AddAsync(batch, cancellationToken);
        await _reconRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(batch);
    }

    public async Task<Result<CashFlowSummaryReport>> GetCashFlowSummaryAsync(
        User actor,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<CashFlowSummaryReport>(guard.Error);

        var ledger = await _cashFlowRepo.ListInPeriodAsync(periodStartUtc, periodEndUtc, cancellationToken);
        var pending = await _entryRepo.ListPendingAsync(cancellationToken);

        var inflows = ledger.Where(l => l.Direction == CashFlowDirection.Inflow).Sum(l => l.Amount.Amount);
        var outflows = ledger.Where(l => l.Direction == CashFlowDirection.Outflow).Sum(l => l.Amount.Amount);
        var netRealized = decimal.Round(inflows - outflows, 2, MidpointRounding.ToEven);

        var pendRec = pending.Where(e => e.Kind == FinancialEntryKind.Receivable).Sum(e => e.RemainingPrincipal.Amount);
        var pendPay = pending.Where(e => e.Kind == FinancialEntryKind.Payable).Sum(e => e.RemainingPrincipal.Amount);
        var projected = decimal.Round(netRealized + pendRec - pendPay, 2, MidpointRounding.ToEven);

        return Result.Success(new CashFlowSummaryReport(
            guard.Value,
            periodStartUtc,
            periodEndUtc,
            inflows,
            outflows,
            netRealized,
            pendRec,
            pendPay,
            projected));
    }

    public async Task<Result<DreStatementReport>> GetDreStatementAsync(
        User actor,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<DreStatementReport>(guard.Error);

        var entries = await _entryRepo.ListAllInPeriodAsync(periodStartUtc, periodEndUtc, cancellationToken);
        return Result.Success(DreCalculator.Compute(guard.Value, periodStartUtc, periodEndUtc, entries));
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de operar financeiro de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Finance, action))
            return Result.Failure<Guid>(Error.Forbidden("Finance.Forbidden", "Permissão insuficiente para operar módulo financeiro."));

        return Result.Success(tenantId);
    }
}
