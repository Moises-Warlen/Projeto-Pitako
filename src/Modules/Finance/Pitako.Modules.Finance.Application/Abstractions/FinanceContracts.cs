using Pitako.Modules.Finance.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Finance.Application.Abstractions;

public sealed record ExternalGatewayChargeRequest(
    string OrderOrSaleReference,
    decimal GrossAmount,
    string CardBrandOrPix,
    string? CardLast4Digits,
    string GatewayTokenReference);

public sealed record ExternalGatewayChargeResponse(
    string ExternalTransactionId,
    string NsuCode,
    decimal FeeAmount);

public sealed record StatementSettlementRow(
    string ExternalTransactionRef,
    decimal StatementNetAmount);

public interface IFinancialEntryRepository
{
    Task<FinancialEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinancialEntry>> ListAllInPeriodAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinancialEntry>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task AddAsync(FinancialEntry entry, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICashFlowLedgerRepository
{
    Task<IReadOnlyList<CashFlowLedgerEntry>> ListInPeriodAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default);
    Task AddAsync(CashFlowLedgerEntry entry, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentGatewayConfigRepository
{
    Task<PaymentGatewayConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentGatewayConfig>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PaymentGatewayConfig config, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentTransactionRepository
{
    Task<IReadOnlyList<PaymentTransactionRecord>> FindByExternalRefsAsync(IEnumerable<string> externalRefs, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentTransactionRecord record, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IReconciliationBatchRepository
{
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SettlementReconciliationBatch batch, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IFinanceSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Cliente HTTP de adquirente / gateway de pagamento autorizado (sempre tokenizado; nunca recebe PAN/CVV completos).
/// </summary>
public interface IPaymentGatewayClient
{
    Task<Result<ExternalGatewayChargeResponse>> ProcessTokenizedChargeAsync(
        PaymentGatewayConfig config,
        string decryptedSecret,
        ExternalGatewayChargeRequest request,
        CancellationToken cancellationToken = default);
}
