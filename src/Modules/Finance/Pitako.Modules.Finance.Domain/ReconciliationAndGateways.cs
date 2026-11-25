using System.Text.RegularExpressions;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Finance.Domain;

public enum PaymentGatewayProvider
{
    Stone = 1,
    Rede = 2,
    Cielo = 3,
    Getnet = 4,
    MercadoPago = 5,
    PagBank = 6,
    PixDirect = 7
}

public enum PaymentTransactionStatus
{
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    Refunded = 4
}

public enum ReconciliationMatchStatus
{
    MatchedExact = 1,
    FeeOrAmountDivergence = 2,
    MissingInInternalLedger = 3
}

/// <summary>
/// Proteção rigorosa contra armazenamento de dados completos de cartão (PCI-DSS):
/// bloqueia qualquer PAN (13 a 19 dígitos) ou CVV em payloads ou descrições.
/// </summary>
public static partial class PciCardDataGuard
{
    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,19}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex PanRegex();

    [GeneratedRegex(@"^\d{4}$", RegexOptions.Compiled)]
    private static partial Regex Last4Regex();

    public static Result EnsureNoRawCardData(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            if (PanRegex().IsMatch(field))
            {
                return Result.Failure(Error.Validation(
                    "Finance.RawCardDataForbidden",
                    "Armazenamento ou envio de número completo de cartão (PAN) é estritamente proibido por conformidade PCI."));
            }
        }

        return Result.Success();
    }

    public static Result<string?> ValidateLast4Digits(string? last4)
    {
        if (string.IsNullOrWhiteSpace(last4))
            return Result.Success<string?>(null);

        var clean = last4.Trim();
        if (!Last4Regex().IsMatch(clean))
            return Result.Failure<string?>(Error.Validation("Finance.InvalidLast4", "Apenas os 4 últimos dígitos numéricos do cartão são permitidos."));

        return Result.Success<string?>(clean);
    }
}

/// <summary>
/// Configuração de adquirente / gateway autorizado por loja com chave cifrada em repouso (Seção 33).
/// </summary>
public sealed class PaymentGatewayConfig : TenantScopedAggregateRoot
{
    private PaymentGatewayConfig()
    {
        MerchantAccountCode = string.Empty;
        EncryptedSecretBlob = string.Empty;
    }

    private PaymentGatewayConfig(
        Guid id,
        Guid tenantId,
        PaymentGatewayProvider provider,
        string merchantAccountCode,
        string encryptedSecretBlob,
        decimal defaultMdrPercent,
        int settlementDays) : base(id, tenantId)
    {
        Provider = provider;
        MerchantAccountCode = merchantAccountCode;
        EncryptedSecretBlob = encryptedSecretBlob;
        DefaultMdrPercent = decimal.Round(defaultMdrPercent, 2, MidpointRounding.ToEven);
        SettlementDays = settlementDays;
        IsActive = true;
    }

    public PaymentGatewayProvider Provider { get; private set; }
    public string MerchantAccountCode { get; private set; }
    public string EncryptedSecretBlob { get; private set; }
    public decimal DefaultMdrPercent { get; private set; }
    public int SettlementDays { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<PaymentGatewayConfig> Create(
        Guid tenantId,
        PaymentGatewayProvider provider,
        string merchantAccountCode,
        string encryptedSecretBlob,
        decimal defaultMdrPercent,
        int settlementDays = 1)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(merchantAccountCode))
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.MerchantCodeRequired", "Código do estabelecimento no gateway é obrigatório."));
        if (string.IsNullOrWhiteSpace(encryptedSecretBlob))
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.GatewaySecretRequired", "Credencial cifrada do gateway é obrigatória."));
        if (defaultMdrPercent is < 0m or > 20m)
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.InvalidMdr", "Taxa MDR padrão deve estar entre 0% e 20%."));
        if (settlementDays is < 0 or > 90)
            return Result.Failure<PaymentGatewayConfig>(Error.Validation("Finance.InvalidSettlementDays", "Prazo de liquidação deve estar entre 0 e 90 dias."));

        return Result.Success(new PaymentGatewayConfig(
            Guid.NewGuid(),
            tenantId,
            provider,
            merchantAccountCode.Trim(),
            encryptedSecretBlob.Trim(),
            defaultMdrPercent,
            settlementDays));
    }
}

/// <summary>
/// Transação tokenizada via gateway autorizado (armazena apenas NSU/ID externo e 4 últimos dígitos, nunca PAN/CVV).
/// </summary>
public sealed class PaymentTransactionRecord : TenantScopedAggregateRoot
{
    private PaymentTransactionRecord()
    {
        OrderOrSaleReference = string.Empty;
        ExternalTransactionId = string.Empty;
        GrossAmount = Money.Zero();
        GatewayFeeAmount = Money.Zero();
        NetAmount = Money.Zero();
    }

    private PaymentTransactionRecord(
        Guid id,
        Guid tenantId,
        Guid gatewayConfigId,
        PaymentGatewayProvider provider,
        string orderOrSaleReference,
        string externalTransactionId,
        string? nsuCode,
        string? cardBrand,
        string? cardLast4Digits,
        Money grossAmount,
        Money gatewayFeeAmount,
        PaymentTransactionStatus status,
        string? failureReason,
        DateTimeOffset processedAtUtc) : base(id, tenantId)
    {
        GatewayConfigId = gatewayConfigId;
        Provider = provider;
        OrderOrSaleReference = orderOrSaleReference;
        ExternalTransactionId = externalTransactionId;
        NsuCode = nsuCode;
        CardBrand = cardBrand;
        CardLast4Digits = cardLast4Digits;
        GrossAmount = Money.Create(grossAmount.Amount, grossAmount.Currency).Value;
        GatewayFeeAmount = Money.Create(gatewayFeeAmount.Amount, gatewayFeeAmount.Currency).Value;
        NetAmount = Money.Create(Math.Max(0m, grossAmount.Amount - gatewayFeeAmount.Amount), grossAmount.Currency).Value;
        Status = status;
        FailureReason = failureReason;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid GatewayConfigId { get; private set; }
    public PaymentGatewayProvider Provider { get; private set; }
    public string OrderOrSaleReference { get; private set; }
    public string ExternalTransactionId { get; private set; }
    public string? NsuCode { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4Digits { get; private set; }
    public Money GrossAmount { get; private set; }
    public Money GatewayFeeAmount { get; private set; }
    public Money NetAmount { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static Result<PaymentTransactionRecord> Create(
        Guid tenantId,
        Guid gatewayConfigId,
        PaymentGatewayProvider provider,
        string orderOrSaleReference,
        string externalTransactionId,
        string? nsuCode,
        string? cardBrand,
        string? cardLast4Digits,
        decimal grossAmount,
        decimal gatewayFeeAmount,
        PaymentTransactionStatus status,
        string? failureReason,
        DateTimeOffset processedAtUtc)
    {
        var pciGuard = PciCardDataGuard.EnsureNoRawCardData(orderOrSaleReference, externalTransactionId, nsuCode, failureReason);
        if (pciGuard.IsFailure)
            return Result.Failure<PaymentTransactionRecord>(pciGuard.Error);

        var last4Res = PciCardDataGuard.ValidateLast4Digits(cardLast4Digits);
        if (last4Res.IsFailure)
            return Result.Failure<PaymentTransactionRecord>(last4Res.Error);

        if (tenantId == Guid.Empty || gatewayConfigId == Guid.Empty)
            return Result.Failure<PaymentTransactionRecord>(Error.Validation("Finance.InvalidGatewayContext", "TenantId e GatewayConfigId são obrigatórios."));
        if (grossAmount <= 0m || gatewayFeeAmount < 0m)
            return Result.Failure<PaymentTransactionRecord>(Error.Validation("Finance.InvalidAmount", "Valores da transação inválidos."));

        return Result.Success(new PaymentTransactionRecord(
            Guid.NewGuid(),
            tenantId,
            gatewayConfigId,
            provider,
            orderOrSaleReference.Trim(),
            externalTransactionId.Trim(),
            string.IsNullOrWhiteSpace(nsuCode) ? null : nsuCode.Trim(),
            string.IsNullOrWhiteSpace(cardBrand) ? null : cardBrand.Trim(),
            last4Res.Value,
            Money.Create(grossAmount).Value,
            Money.Create(gatewayFeeAmount).Value,
            status,
            string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim(),
            processedAtUtc));
    }
}

/// <summary>
/// Linha individual de conciliação entre extrato de adquirente/marketplace e razão interna (Seção 33).
/// </summary>
public sealed class ReconciliationLine : Entity<Guid>, ITenantScopedEntity
{
    private ReconciliationLine()
    {
        ExternalTransactionRef = string.Empty;
        InternalNetAmount = Money.Zero();
        StatementNetAmount = Money.Zero();
        VarianceAmount = Money.Zero();
    }

    internal ReconciliationLine(
        Guid id,
        Guid tenantId,
        Guid batchId,
        string externalTransactionRef,
        Money internalNetAmount,
        Money statementNetAmount,
        ReconciliationMatchStatus matchStatus,
        string? notes) : base(id)
    {
        TenantId = tenantId;
        BatchId = batchId;
        ExternalTransactionRef = externalTransactionRef;
        InternalNetAmount = Money.Create(internalNetAmount.Amount, internalNetAmount.Currency).Value;
        StatementNetAmount = Money.Create(statementNetAmount.Amount, statementNetAmount.Currency).Value;
        VarianceAmount = Money.Create(Math.Abs(statementNetAmount.Amount - internalNetAmount.Amount), internalNetAmount.Currency).Value;
        MatchStatus = matchStatus;
        Notes = notes;
    }

    public Guid TenantId { get; private set; }
    public Guid BatchId { get; private set; }
    public string ExternalTransactionRef { get; private set; }
    public Money InternalNetAmount { get; private set; }
    public Money StatementNetAmount { get; private set; }
    public Money VarianceAmount { get; private set; }
    public ReconciliationMatchStatus MatchStatus { get; private set; }
    public string? Notes { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em ReconciliationLine.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Lote de conciliação financeira entre extratos externos e títulos/transações internas (Seção 33).
/// </summary>
public sealed class SettlementReconciliationBatch : TenantScopedAggregateRoot
{
    private readonly List<ReconciliationLine> _lines = new();

    private SettlementReconciliationBatch()
    {
        BatchCode = string.Empty;
        ProviderName = string.Empty;
        TotalVarianceAmount = Money.Zero();
    }

    private SettlementReconciliationBatch(
        Guid id,
        Guid tenantId,
        string batchCode,
        string providerName,
        DateTimeOffset executedAtUtc) : base(id, tenantId)
    {
        BatchCode = batchCode;
        ProviderName = providerName;
        ExecutedAtUtc = executedAtUtc;
        TotalVarianceAmount = Money.Zero();
    }

    public string BatchCode { get; private set; }
    public string ProviderName { get; private set; }
    public DateTimeOffset ExecutedAtUtc { get; private set; }
    public int ExactMatchesCount { get; private set; }
    public int DivergencesCount { get; private set; }
    public Money TotalVarianceAmount { get; private set; }
    public IReadOnlyCollection<ReconciliationLine> Lines => _lines.AsReadOnly();

    public static SettlementReconciliationBatch Start(Guid tenantId, string batchCode, string providerName, DateTimeOffset utcNow) =>
        new(Guid.NewGuid(), tenantId, batchCode.Trim().ToUpperInvariant(), providerName.Trim(), utcNow);

    public void AddLine(
        string externalRef,
        decimal internalNet,
        decimal statementNet,
        bool foundInInternal)
    {
        var diff = decimal.Round(statementNet - internalNet, 2, MidpointRounding.ToEven);
        var status = !foundInInternal
            ? ReconciliationMatchStatus.MissingInInternalLedger
            : diff == 0m
                ? ReconciliationMatchStatus.MatchedExact
                : ReconciliationMatchStatus.FeeOrAmountDivergence;

        var notes = status switch
        {
            ReconciliationMatchStatus.MatchedExact => "Conciliado 100%",
            ReconciliationMatchStatus.MissingInInternalLedger => "Transação presente no extrato externo e ausente no livro interno.",
            _ => $"Diferença líquida de R$ {diff:N2}"
        };

        _lines.Add(new ReconciliationLine(
            Guid.NewGuid(),
            TenantId,
            Id,
            externalRef.Trim(),
            Money.Create(Math.Max(0m, internalNet)).Value,
            Money.Create(Math.Max(0m, statementNet)).Value,
            status,
            notes));

        ExactMatchesCount = _lines.Count(l => l.MatchStatus == ReconciliationMatchStatus.MatchedExact);
        DivergencesCount = _lines.Count(l => l.MatchStatus != ReconciliationMatchStatus.MatchedExact);
        TotalVarianceAmount = Money.Create(_lines.Sum(l => l.VarianceAmount.Amount)).Value;
    }
}
