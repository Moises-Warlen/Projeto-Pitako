using Pitako.Modules.Marketplaces.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Marketplaces.Application.Abstractions;

public interface IMarketplacePartnerAccountRepository
{
    Task<MarketplacePartnerAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplacePartnerAccount>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MarketplacePartnerAccount account, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMarketplaceStoreBindingRepository
{
    Task<MarketplaceStoreBinding?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MarketplaceStoreBinding?> FindByProviderAndMerchantAsync(MarketplaceProvider provider, string externalMerchantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplaceStoreBinding>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplaceStoreBinding>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MarketplaceStoreBinding binding, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMarketplaceInboundEventRepository
{
    Task<MarketplaceInboundEvent?> FindByExternalEventIdAsync(MarketplaceProvider provider, string externalEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplaceInboundEvent>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task AddAsync(MarketplaceInboundEvent inboundEvent, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMarketplaceCatalogMappingRepository
{
    Task<MarketplaceCatalogMapping?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplaceCatalogMapping>> ListByBindingAsync(Guid storeBindingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketplaceCatalogMapping>> ListByInternalProductAsync(Guid internalProductId, CancellationToken cancellationToken = default);
    Task AddAsync(MarketplaceCatalogMapping mapping, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cifragem autenticada de credenciais de parceiros (ClientSecret / ApiKey / Tokens) em repouso.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Porta HTTP remota para APIs dos parceiros marketplace; quando sem credenciais reais externas, falha explicitamente.
/// </summary>
public interface IMarketplaceRemoteGateway
{
    Task<Result<string>> ExecuteRemoteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato de conector específico por provedor marketplace no Hub PITAKO (Seção 28).
/// </summary>
public interface IMarketplaceConnector
{
    MarketplaceProvider Provider { get; }
    MarketplaceCapabilities Capabilities { get; }
    bool VerifyWebhookSignature(string rawBody, string? signatureHeader, string secretKey);
    Result<MarketplaceNormalizedOrderPayload> NormalizeOrderWebhook(string rawBody, string externalMerchantId);
    Task<Result<string>> ExecuteActionAsync(
        MarketplacePartnerAccount account,
        MarketplaceStoreBinding binding,
        MarketplaceActionKind action,
        string? targetExternalId,
        string payloadJson,
        IMarketplaceRemoteGateway remoteGateway,
        CancellationToken cancellationToken = default);
}
