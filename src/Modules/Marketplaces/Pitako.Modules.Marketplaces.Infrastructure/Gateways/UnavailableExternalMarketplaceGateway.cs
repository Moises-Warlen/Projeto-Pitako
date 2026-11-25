using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Marketplaces.Infrastructure.Gateways;

/// <summary>
/// Gateway padrão quando o ambiente não possui chaves/credenciais HTTP oficiais de parceiros configuradas.
/// Nunca simula chamadas remotas bem-sucedidas: informa explicitamente o bloqueio de integração externa (Seção 28).
/// </summary>
public sealed class UnavailableExternalMarketplaceGateway : IMarketplaceRemoteGateway
{
    public Task<Result<string>> ExecuteRemoteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(Error.Conflict(
            "Marketplace.ExternalCredentialsNotConfigured",
            $"Integração HTTP externa com {binding.Provider} (loja '{binding.ExternalStoreName}') indisponível sem credenciais de produção configuradas.")));
}
