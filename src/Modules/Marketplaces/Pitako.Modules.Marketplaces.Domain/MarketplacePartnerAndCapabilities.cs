using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Marketplaces.Domain;

public enum MarketplaceProvider
{
    IFood = 1,
    Rappi = 2,
    Aiqfome = 3,
    DeliveryMuch = 4,
    Food99 = 5,
    Keeta = 6,
    FoodToSave = 7,
    MenuDino = 8,
    OwnDeliveryWebhook = 9
}

public enum MarketplaceAuthKind
{
    OAuth2ClientCredentials = 1,
    ApiKey = 2,
    BearerPartnerToken = 3
}

public enum MarketplaceBindingStatus
{
    Connected = 1,
    Paused = 2,
    TokenExpired = 3,
    Disconnected = 4,
    Error = 5
}

public enum MarketplaceActionKind
{
    ConfirmOrder = 1,
    DispatchOrder = 2,
    CancelOrder = 3,
    ToggleItemAvailability = 4,
    SyncItemPrice = 5,
    ToggleStoreOpenClose = 6
}

/// <summary>
/// Matriz declarativa das capacidades reais suportadas pela API oficial de cada marketplace (Seção 28).
/// Impede que o Hub tente simular operações inexistentes no parceiro.
/// </summary>
public sealed record MarketplaceCapabilities(
    MarketplaceProvider Provider,
    string DisplayName,
    MarketplaceAuthKind AuthKind,
    bool SupportsOrderConfirmation,
    bool SupportsOrderDispatch,
    bool SupportsOrderCancellation,
    bool SupportsItemPauseToggle,
    bool SupportsCatalogPriceSync,
    bool SupportsStoreOpenCloseToggle,
    bool SupportsWebhookHmacSignature,
    bool SupportsBulkMenuActions,
    int MaxBatchSize)
{
    public bool SupportsAction(MarketplaceActionKind action) => action switch
    {
        MarketplaceActionKind.ConfirmOrder => SupportsOrderConfirmation,
        MarketplaceActionKind.DispatchOrder => SupportsOrderDispatch,
        MarketplaceActionKind.CancelOrder => SupportsOrderCancellation,
        MarketplaceActionKind.ToggleItemAvailability => SupportsItemPauseToggle,
        MarketplaceActionKind.SyncItemPrice => SupportsCatalogPriceSync,
        MarketplaceActionKind.ToggleStoreOpenClose => SupportsStoreOpenCloseToggle,
        _ => false
    };
}

public static class MarketplaceCapabilityMatrix
{
    private static readonly IReadOnlyDictionary<MarketplaceProvider, MarketplaceCapabilities> Matrix =
        new Dictionary<MarketplaceProvider, MarketplaceCapabilities>
        {
            [MarketplaceProvider.IFood] = new(
                MarketplaceProvider.IFood, "iFood", MarketplaceAuthKind.OAuth2ClientCredentials,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: true, SupportsBulkMenuActions: true, MaxBatchSize: 200),

            [MarketplaceProvider.Rappi] = new(
                MarketplaceProvider.Rappi, "Rappi", MarketplaceAuthKind.OAuth2ClientCredentials,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: true, SupportsBulkMenuActions: true, MaxBatchSize: 100),

            [MarketplaceProvider.Aiqfome] = new(
                MarketplaceProvider.Aiqfome, "Aiqfome", MarketplaceAuthKind.OAuth2ClientCredentials,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: false, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: false, SupportsBulkMenuActions: false, MaxBatchSize: 25),

            [MarketplaceProvider.DeliveryMuch] = new(
                MarketplaceProvider.DeliveryMuch, "Delivery Much", MarketplaceAuthKind.ApiKey,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: false,
                SupportsWebhookHmacSignature: false, SupportsBulkMenuActions: false, MaxBatchSize: 20),

            [MarketplaceProvider.Food99] = new(
                MarketplaceProvider.Food99, "99Food", MarketplaceAuthKind.OAuth2ClientCredentials,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: true, SupportsBulkMenuActions: true, MaxBatchSize: 100),

            [MarketplaceProvider.Keeta] = new(
                MarketplaceProvider.Keeta, "Keeta", MarketplaceAuthKind.OAuth2ClientCredentials,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: true, SupportsBulkMenuActions: true, MaxBatchSize: 100),

            [MarketplaceProvider.FoodToSave] = new(
                MarketplaceProvider.FoodToSave, "Food To Save", MarketplaceAuthKind.ApiKey,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: false, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: false, SupportsBulkMenuActions: false, MaxBatchSize: 20),

            [MarketplaceProvider.MenuDino] = new(
                MarketplaceProvider.MenuDino, "MenuDino", MarketplaceAuthKind.ApiKey,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: false, SupportsBulkMenuActions: false, MaxBatchSize: 50),

            [MarketplaceProvider.OwnDeliveryWebhook] = new(
                MarketplaceProvider.OwnDeliveryWebhook, "Delivery Próprio (Webhook / API)", MarketplaceAuthKind.ApiKey,
                SupportsOrderConfirmation: true, SupportsOrderDispatch: true, SupportsOrderCancellation: true,
                SupportsItemPauseToggle: true, SupportsCatalogPriceSync: true, SupportsStoreOpenCloseToggle: true,
                SupportsWebhookHmacSignature: false, SupportsBulkMenuActions: true, MaxBatchSize: 200)
        };

    public static MarketplaceCapabilities GetFor(MarketplaceProvider provider) =>
        Matrix.TryGetValue(provider, out var caps)
            ? caps
            : throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provedor de marketplace não mapeado.");

    public static IReadOnlyList<MarketplaceCapabilities> ListAll() =>
        Matrix.Values.OrderBy(c => (int)c.Provider).ToList();
}

/// <summary>
/// Conta de integração com parceiro marketplace por estabelecimento, mantendo credenciais criptografadas em repouso.
/// </summary>
public sealed class MarketplacePartnerAccount : TenantScopedAggregateRoot
{
    private MarketplacePartnerAccount()
    {
        AccountLabel = string.Empty;
        ClientOrMerchantIdentifier = string.Empty;
        EncryptedCredentialsBlob = string.Empty;
    }

    private MarketplacePartnerAccount(
        Guid id,
        Guid tenantId,
        MarketplaceProvider provider,
        string accountLabel,
        string clientOrMerchantIdentifier,
        string encryptedCredentialsBlob) : base(id, tenantId)
    {
        Provider = provider;
        AccountLabel = accountLabel;
        ClientOrMerchantIdentifier = clientOrMerchantIdentifier;
        EncryptedCredentialsBlob = encryptedCredentialsBlob;
        AuthKind = MarketplaceCapabilityMatrix.GetFor(provider).AuthKind;
        IsActive = true;
    }

    public MarketplaceProvider Provider { get; private set; }
    public MarketplaceAuthKind AuthKind { get; private set; }
    public string AccountLabel { get; private set; }
    public string ClientOrMerchantIdentifier { get; private set; }
    public string EncryptedCredentialsBlob { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastHealthCheckUtc { get; private set; }

    public static Result<MarketplacePartnerAccount> Create(
        Guid tenantId,
        MarketplaceProvider provider,
        string accountLabel,
        string clientOrMerchantIdentifier,
        string encryptedCredentialsBlob)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<MarketplacePartnerAccount>(Error.Validation("Marketplace.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(accountLabel))
            return Result.Failure<MarketplacePartnerAccount>(Error.Validation("Marketplace.AccountLabelRequired", "Identificação da conta parceira é obrigatória."));
        if (string.IsNullOrWhiteSpace(clientOrMerchantIdentifier))
            return Result.Failure<MarketplacePartnerAccount>(Error.Validation("Marketplace.ClientIdRequired", "ClientId ou identificador do parceiro é obrigatório."));
        if (string.IsNullOrWhiteSpace(encryptedCredentialsBlob))
            return Result.Failure<MarketplacePartnerAccount>(Error.Validation("Marketplace.SecretRequired", "Payload criptografado de credenciais é obrigatório."));

        return Result.Success(new MarketplacePartnerAccount(
            Guid.NewGuid(),
            tenantId,
            provider,
            accountLabel.Trim(),
            clientOrMerchantIdentifier.Trim(),
            encryptedCredentialsBlob.Trim()));
    }

    public void RotateEncryptedCredentials(string newEncryptedBlob)
    {
        if (string.IsNullOrWhiteSpace(newEncryptedBlob))
            throw new ArgumentException("Credencial criptografada não pode ser vazia.", nameof(newEncryptedBlob));

        EncryptedCredentialsBlob = newEncryptedBlob.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void RecordHealthCheck(DateTimeOffset utcNow) => LastHealthCheckUtc = utcNow;
}

/// <summary>
/// Vínculo de uma loja/filial externa de marketplace com o estabelecimento/unidade PITAKO (Seção 28).
/// </summary>
public sealed class MarketplaceStoreBinding : TenantScopedAggregateRoot
{
    private MarketplaceStoreBinding()
    {
        ExternalMerchantId = string.Empty;
        ExternalStoreName = string.Empty;
    }

    private MarketplaceStoreBinding(
        Guid id,
        Guid tenantId,
        Guid partnerAccountId,
        MarketplaceProvider provider,
        Guid? storeUnitId,
        string externalMerchantId,
        string externalStoreName,
        bool autoAcceptOrders) : base(id, tenantId)
    {
        PartnerAccountId = partnerAccountId;
        Provider = provider;
        StoreUnitId = storeUnitId;
        ExternalMerchantId = externalMerchantId;
        ExternalStoreName = externalStoreName;
        AutoAcceptOrders = autoAcceptOrders;
        Status = MarketplaceBindingStatus.Connected;
    }

    public Guid PartnerAccountId { get; private set; }
    public MarketplaceProvider Provider { get; private set; }
    public Guid? StoreUnitId { get; private set; }
    public string ExternalMerchantId { get; private set; }
    public string ExternalStoreName { get; private set; }
    public MarketplaceBindingStatus Status { get; private set; }
    public bool AutoAcceptOrders { get; private set; }
    public DateTimeOffset? LastSyncUtc { get; private set; }
    public string? LastError { get; private set; }

    public static Result<MarketplaceStoreBinding> Create(
        Guid tenantId,
        MarketplacePartnerAccount account,
        string externalMerchantId,
        string externalStoreName,
        Guid? storeUnitId = null,
        bool autoAcceptOrders = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (tenantId == Guid.Empty)
            return Result.Failure<MarketplaceStoreBinding>(Error.Validation("Marketplace.TenantRequired", "TenantId é obrigatório."));
        if (account.TenantId != tenantId)
            return Result.Failure<MarketplaceStoreBinding>(Error.Forbidden("Marketplace.CrossTenantAccount", "Conta parceira pertence a outro estabelecimento."));
        if (string.IsNullOrWhiteSpace(externalMerchantId))
            return Result.Failure<MarketplaceStoreBinding>(Error.Validation("Marketplace.ExternalMerchantRequired", "Identificador externo da loja (merchantId) é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalStoreName))
            return Result.Failure<MarketplaceStoreBinding>(Error.Validation("Marketplace.ExternalStoreNameRequired", "Nome externo da loja no parceiro é obrigatório."));

        return Result.Success(new MarketplaceStoreBinding(
            Guid.NewGuid(),
            tenantId,
            account.Id,
            account.Provider,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            externalMerchantId.Trim(),
            externalStoreName.Trim(),
            autoAcceptOrders));
    }

    public void Pause(string? reason = null)
    {
        Status = MarketplaceBindingStatus.Paused;
        LastError = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Reconnect(DateTimeOffset utcNow)
    {
        Status = MarketplaceBindingStatus.Connected;
        LastError = null;
        LastSyncUtc = utcNow;
    }

    public void RecordSyncFailure(string error, DateTimeOffset utcNow, bool tokenExpired = false)
    {
        Status = tokenExpired ? MarketplaceBindingStatus.TokenExpired : MarketplaceBindingStatus.Error;
        LastError = string.IsNullOrWhiteSpace(error) ? "Falha de sincronização." : error.Trim();
        LastSyncUtc = utcNow;
    }
}
