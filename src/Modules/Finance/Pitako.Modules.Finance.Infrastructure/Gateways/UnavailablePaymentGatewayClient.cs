using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Finance.Infrastructure.Gateways;

/// <summary>
/// Cliente padrão quando credenciais reais de produção dos adquirentes (Stone, Rede, Cielo, MercadoPago, etc.)
/// não estão configuradas neste ambiente. Nunca simula cobrança autorizada: retorna falha explícita (Seção 33).
/// </summary>
public sealed class UnavailablePaymentGatewayClient : IPaymentGatewayClient
{
    public Task<Result<ExternalGatewayChargeResponse>> ProcessTokenizedChargeAsync(
        PaymentGatewayConfig config,
        string decryptedSecret,
        ExternalGatewayChargeRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<ExternalGatewayChargeResponse>(Error.Conflict(
            "Finance.GatewayCredentialsNotConfigured",
            $"Gateway {config.Provider} (conta '{config.MerchantAccountCode}') indisponível neste ambiente sem credenciais de produção configuradas.")));
}
