using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Finance.Domain;

public enum FinancialEntryKind
{
    Receivable = 1,
    Payable = 2
}

public enum FinancialEntryStatus
{
    Open = 1,
    PartiallySettled = 2,
    Settled = 3,
    Cancelled = 4
}

public enum DreLineCategory
{
    GrossSalesRevenue = 1,
    MarketplaceAndGatewayFees = 2,
    CogsSuppliesAndInputs = 3,
    PayrollAndStaff = 4,
    RentAndUtilities = 5,
    MarketingAndOtherOpex = 6
}

public enum CashFlowDirection
{
    Inflow = 1,
    Outflow = 2
}

/// <summary>
/// Título financeiro (Conta a Receber ou Conta a Pagar) com rastreio de juros/taxas e classificação DRE (Seção 33).
/// </summary>
public sealed class FinancialEntry : TenantScopedAggregateRoot
{
    private FinancialEntry()
    {
        EntryCode = string.Empty;
        CounterpartyName = string.Empty;
        Description = string.Empty;
        OriginalAmount = Money.Zero();
        FeeOrAdjustmentAmount = Money.Zero();
        SettledAmount = Money.Zero();
    }

    private FinancialEntry(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string entryCode,
        FinancialEntryKind kind,
        DreLineCategory dreCategory,
        string counterpartyName,
        string description,
        string? referenceDocument,
        Money originalAmount,
        DateTimeOffset dueDateUtc) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        EntryCode = entryCode;
        Kind = kind;
        DreCategory = dreCategory;
        CounterpartyName = counterpartyName;
        Description = description;
        ReferenceDocument = referenceDocument;
        OriginalAmount = Money.Create(originalAmount.Amount, originalAmount.Currency).Value;
        FeeOrAdjustmentAmount = Money.Zero(originalAmount.Currency);
        SettledAmount = Money.Zero(originalAmount.Currency);
        DueDateUtc = dueDateUtc;
        Status = FinancialEntryStatus.Open;
    }

    public Guid? StoreUnitId { get; private set; }
    public string EntryCode { get; private set; }
    public FinancialEntryKind Kind { get; private set; }
    public DreLineCategory DreCategory { get; private set; }
    public string CounterpartyName { get; private set; }
    public string Description { get; private set; }
    public string? ReferenceDocument { get; private set; }
    public Money OriginalAmount { get; private set; }
    public Money FeeOrAdjustmentAmount { get; private set; }
    public Money SettledAmount { get; private set; }
    public DateTimeOffset DueDateUtc { get; private set; }
    public FinancialEntryStatus Status { get; private set; }
    public DateTimeOffset? SettledAtUtc { get; private set; }
    public string? PaymentMethodName { get; private set; }

    public Money RemainingPrincipal =>
        Money.Create(Math.Max(0m, OriginalAmount.Amount - FeeOrAdjustmentAmount.Amount - SettledAmount.Amount), OriginalAmount.Currency).Value;

    public static Result<FinancialEntry> Create(
        Guid tenantId,
        string entryCode,
        FinancialEntryKind kind,
        DreLineCategory dreCategory,
        string counterpartyName,
        string description,
        decimal originalAmount,
        DateTimeOffset dueDateUtc,
        string? referenceDocument = null,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<FinancialEntry>(Error.Validation("Finance.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(entryCode))
            return Result.Failure<FinancialEntry>(Error.Validation("Finance.EntryCodeRequired", "Código do lançamento é obrigatório."));
        if (string.IsNullOrWhiteSpace(counterpartyName))
            return Result.Failure<FinancialEntry>(Error.Validation("Finance.CounterpartyRequired", "Cliente/Fornecedor é obrigatório."));
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<FinancialEntry>(Error.Validation("Finance.DescriptionRequired", "Descrição do título é obrigatória."));
        if (originalAmount <= 0m)
            return Result.Failure<FinancialEntry>(Error.Validation("Finance.InvalidAmount", "Valor do título deve ser positivo."));

        var money = Money.Create(originalAmount);
        if (money.IsFailure)
            return Result.Failure<FinancialEntry>(money.Error);

        return Result.Success(new FinancialEntry(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            entryCode.Trim().ToUpperInvariant(),
            kind,
            dreCategory,
            counterpartyName.Trim(),
            description.Trim(),
            string.IsNullOrWhiteSpace(referenceDocument) ? null : referenceDocument.Trim(),
            money.Value,
            dueDateUtc));
    }

    public Result RecordSettlement(Money netPaidAmount, Money feeOrDiscountAmount, string paymentMethod, DateTimeOffset settledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(netPaidAmount);
        ArgumentNullException.ThrowIfNull(feeOrDiscountAmount);

        if (Status is FinancialEntryStatus.Settled or FinancialEntryStatus.Cancelled)
            return Result.Failure(Error.Conflict("Finance.AlreadyClosed", "Título já baixado ou cancelado."));
        if (netPaidAmount.Amount <= 0m)
            return Result.Failure(Error.Validation("Finance.InvalidSettlement", "Valor liquidado deve ser positivo."));
        if (string.IsNullOrWhiteSpace(paymentMethod))
            return Result.Failure(Error.Validation("Finance.PaymentMethodRequired", "Forma de pagamento da baixa é obrigatória."));

        var totalCredit = netPaidAmount.Amount + feeOrDiscountAmount.Amount;
        if (totalCredit > RemainingPrincipal.Amount)
            return Result.Failure(Error.Validation("Finance.OverSettlement", "Baixa informada excede o saldo em aberto do título."));

        FeeOrAdjustmentAmount = FeeOrAdjustmentAmount.Add(feeOrDiscountAmount);
        SettledAmount = SettledAmount.Add(netPaidAmount);
        PaymentMethodName = paymentMethod.Trim();
        SettledAtUtc = settledAtUtc;
        Status = RemainingPrincipal.Amount == 0m ? FinancialEntryStatus.Settled : FinancialEntryStatus.PartiallySettled;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == FinancialEntryStatus.Settled || SettledAmount.Amount > 0m)
            return Result.Failure(Error.Conflict("Finance.CannotCancelSettled", "Título com liquidação registrada não pode ser cancelado."));

        Status = FinancialEntryStatus.Cancelled;
        return Result.Success();
    }
}

/// <summary>
/// Lançamento realizado no livro de Fluxo de Caixa do estabelecimento (Seção 33).
/// </summary>
public sealed class CashFlowLedgerEntry : TenantScopedAggregateRoot
{
    private CashFlowLedgerEntry()
    {
        Description = string.Empty;
        PaymentMethodName = string.Empty;
        Amount = Money.Zero();
    }

    private CashFlowLedgerEntry(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        Guid? financialEntryId,
        CashFlowDirection direction,
        string description,
        Money amount,
        string paymentMethodName,
        DateTimeOffset occurredAtUtc) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        FinancialEntryId = financialEntryId;
        Direction = direction;
        Description = description;
        Amount = Money.Create(amount.Amount, amount.Currency).Value;
        PaymentMethodName = paymentMethodName;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid? StoreUnitId { get; private set; }
    public Guid? FinancialEntryId { get; private set; }
    public CashFlowDirection Direction { get; private set; }
    public string Description { get; private set; }
    public Money Amount { get; private set; }
    public string PaymentMethodName { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static Result<CashFlowLedgerEntry> Record(
        Guid tenantId,
        CashFlowDirection direction,
        string description,
        Money amount,
        string paymentMethodName,
        DateTimeOffset occurredAtUtc,
        Guid? financialEntryId = null,
        Guid? storeUnitId = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (tenantId == Guid.Empty)
            return Result.Failure<CashFlowLedgerEntry>(Error.Validation("Finance.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<CashFlowLedgerEntry>(Error.Validation("Finance.DescriptionRequired", "Histórico do fluxo de caixa é obrigatório."));
        if (amount.Amount <= 0m)
            return Result.Failure<CashFlowLedgerEntry>(Error.Validation("Finance.InvalidAmount", "Valor deve ser positivo."));

        return Result.Success(new CashFlowLedgerEntry(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            financialEntryId == Guid.Empty ? null : financialEntryId,
            direction,
            description.Trim(),
            amount,
            string.IsNullOrWhiteSpace(paymentMethodName) ? "Outros" : paymentMethodName.Trim(),
            occurredAtUtc));
    }
}

public sealed record DreStatementReport(
    Guid TenantId,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    decimal GrossRevenue,
    decimal DeductionsAndFees,
    decimal NetRevenue,
    decimal CogsAmount,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal OperatingExpenses,
    decimal OperatingResult,
    decimal NetMarginPercent);

public sealed record CashFlowSummaryReport(
    Guid TenantId,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    decimal RealizedInflows,
    decimal RealizedOutflows,
    decimal NetRealizedCashFlow,
    decimal PendingReceivables,
    decimal PendingPayables,
    decimal ProjectedNetBalance);

/// <summary>
/// Calculadora determinística de DRE Gerencial por estabelecimento (Seção 33).
/// </summary>
public static class DreCalculator
{
    public static DreStatementReport Compute(
        Guid tenantId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        IReadOnlyList<FinancialEntry> entriesInPeriod)
    {
        var active = entriesInPeriod.Where(e => e.Status != FinancialEntryStatus.Cancelled).ToList();

        var grossRevenue = active
            .Where(e => e.Kind == FinancialEntryKind.Receivable && e.DreCategory == DreLineCategory.GrossSalesRevenue)
            .Sum(e => e.OriginalAmount.Amount);

        var deductions = active
            .Where(e => e.DreCategory == DreLineCategory.MarketplaceAndGatewayFees)
            .Sum(e => e.Kind == FinancialEntryKind.Payable ? e.OriginalAmount.Amount : e.FeeOrAdjustmentAmount.Amount)
            + active.Where(e => e.Kind == FinancialEntryKind.Receivable && e.DreCategory == DreLineCategory.GrossSalesRevenue)
                    .Sum(e => e.FeeOrAdjustmentAmount.Amount);

        var netRevenue = decimal.Round(grossRevenue - deductions, 2, MidpointRounding.ToEven);

        var cogs = active
            .Where(e => e.Kind == FinancialEntryKind.Payable && e.DreCategory == DreLineCategory.CogsSuppliesAndInputs)
            .Sum(e => e.OriginalAmount.Amount);

        var grossProfit = decimal.Round(netRevenue - cogs, 2, MidpointRounding.ToEven);
        var grossMargin = netRevenue > 0m ? decimal.Round((grossProfit / netRevenue) * 100m, 2, MidpointRounding.ToEven) : 0m;

        var opex = active
            .Where(e => e.Kind == FinancialEntryKind.Payable && e.DreCategory is DreLineCategory.PayrollAndStaff or DreLineCategory.RentAndUtilities or DreLineCategory.MarketingAndOtherOpex)
            .Sum(e => e.OriginalAmount.Amount);

        var operatingResult = decimal.Round(grossProfit - opex, 2, MidpointRounding.ToEven);
        var netMargin = netRevenue > 0m ? decimal.Round((operatingResult / netRevenue) * 100m, 2, MidpointRounding.ToEven) : 0m;

        return new DreStatementReport(
            tenantId,
            periodStartUtc,
            periodEndUtc,
            grossRevenue,
            deductions,
            netRevenue,
            cogs,
            grossProfit,
            grossMargin,
            opex,
            operatingResult,
            netMargin);
    }
}
