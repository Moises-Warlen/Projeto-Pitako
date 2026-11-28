using System.Text.Json;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Connectors;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Marketplaces.Application.Services;

/// <summary>
/// Serviço central do Hub de Marketplaces (Fase 19): cadastro seguro de credenciais com cifragem,
/// vinculação de lojas externas, recepção idempotente de webhooks com verificação HMAC,
/// normalização canônica para os 7 parceiros e despacho com checagem rigorosa de capacidades (Seção 28).
/// </summary>
public sealed class MarketplaceHubService
{
    private readonly IMarketplacePartnerAccountRepository _accountRepo;
    private readonly IMarketplaceStoreBindingRepository _bindingRepo;
    private readonly IMarketplaceInboundEventRepository _eventRepo;
    private readonly IMarketplaceCatalogMappingRepository _mappingRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly MarketplaceConnectorRegistry _connectorRegistry;
    private readonly IMarketplaceRemoteGateway _remoteGateway;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public MarketplaceHubService(
        IMarketplacePartnerAccountRepository accountRepo,
        IMarketplaceStoreBindingRepository bindingRepo,
        IMarketplaceInboundEventRepository eventRepo,
        IMarketplaceCatalogMappingRepository mappingRepo,
        ISecretProtector secretProtector,
        MarketplaceConnectorRegistry connectorRegistry,
        IMarketplaceRemoteGateway remoteGateway,
        ITenantContext tenantContext,
        IClock clock)
    {
        _accountRepo = accountRepo;
        _bindingRepo = bindingRepo;
        _eventRepo = eventRepo;
        _mappingRepo = mappingRepo;
        _secretProtector = secretProtector;
        _connectorRegistry = connectorRegistry;
        _remoteGateway = remoteGateway;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public IReadOnlyList<MarketplaceCapabilities> ListCapabilitiesMatrix() =>
        _connectorRegistry.ListAllCapabilities();

    public async Task<Result<MarketplacePartnerAccount>> ConnectPartnerAccountAsync(
        User actor,
        MarketplaceProvider provider,
        string accountLabel,
        string clientOrMerchantIdentifier,
        string rawSecretPayload,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<MarketplacePartnerAccount>(guard.Error);

        if (string.IsNullOrWhiteSpace(rawSecretPayload))
            return Result.Failure<MarketplacePartnerAccount>(Error.Validation("Marketplace.SecretRequired", "Credencial/token secreto do parceiro é obrigatório."));

        var encrypted = _secretProtector.Protect(rawSecretPayload.Trim());
        var res = MarketplacePartnerAccount.Create(
            guard.Value,
            provider,
            accountLabel,
            clientOrMerchantIdentifier,
            encrypted);

        if (res.IsFailure)
            return res;

        await _accountRepo.AddAsync(res.Value, cancellationToken);
        await _accountRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<MarketplaceStoreBinding>> BindExternalStoreAsync(
        User actor,
        Guid partnerAccountId,
        string externalMerchantId,
        string externalStoreName,
        Guid? storeUnitId = null,
        bool autoAcceptOrders = false,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceStoreBinding>(guard.Error);

        var account = await _accountRepo.FindByIdAsync(partnerAccountId, cancellationToken);
        if (account is null)
            return Result.Failure<MarketplaceStoreBinding>(Error.NotFound("Marketplace.AccountNotFound", "Conta parceira não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, account);

        var existing = await _bindingRepo.FindByProviderAndMerchantAsync(account.Provider, externalMerchantId.Trim(), cancellationToken);
        if (existing is not null)
            return Result.Failure<MarketplaceStoreBinding>(Error.Conflict("Marketplace.DuplicateStoreBinding", $"Loja externa '{externalMerchantId}' já vinculada para {account.Provider}."));

        var res = MarketplaceStoreBinding.Create(
            guard.Value,
            account,
            externalMerchantId,
            externalStoreName,
            storeUnitId,
            autoAcceptOrders);

        if (res.IsFailure)
            return res;

        await _bindingRepo.AddAsync(res.Value, cancellationToken);
        await _bindingRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<MarketplaceCatalogMapping>> MapCatalogProductAsync(
        User actor,
        Guid storeBindingId,
        Guid internalProductId,
        string internalItemTitle,
        string externalSkuCode,
        decimal currentPrice,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceCatalogMapping>(guard.Error);

        var binding = await _bindingRepo.FindByIdAsync(storeBindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<MarketplaceCatalogMapping>(Error.NotFound("Marketplace.StoreBindingNotFound", "Vínculo de loja externa não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, binding);

        var res = MarketplaceCatalogMapping.Create(
            guard.Value,
            binding,
            internalProductId,
            internalItemTitle,
            externalSkuCode,
            currentPrice);

        if (res.IsFailure)
            return res;

        await _mappingRepo.AddAsync(res.Value, cancellationToken);
        await _mappingRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<MarketplaceInboundEvent>> ReceiveAndNormalizeWebhookAsync(
        MarketplaceProvider provider,
        string externalEventId,
        string externalMerchantId,
        string eventType,
        string rawPayloadJson,
        string? signatureHeader = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(externalEventId))
            return Result.Failure<MarketplaceInboundEvent>(Error.Validation("Marketplace.EventIdRequired", "ExternalEventId é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalMerchantId))
            return Result.Failure<MarketplaceInboundEvent>(Error.Validation("Marketplace.ExternalMerchantRequired", "ExternalMerchantId é obrigatório."));

        var existingEvent = await _eventRepo.FindByExternalEventIdAsync(provider, externalEventId.Trim(), cancellationToken);
        if (existingEvent is not null)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existingEvent);
            return Result.Success(existingEvent);
        }

        var binding = await _bindingRepo.FindByProviderAndMerchantAsync(provider, externalMerchantId.Trim(), cancellationToken);
        if (binding is null)
            return Result.Failure<MarketplaceInboundEvent>(Error.NotFound("Marketplace.StoreBindingNotFound", $"Nenhuma loja do tenant vinculada ao merchant '{externalMerchantId}' em {provider}."));

        TenantGuard.EnsureOwnership(_tenantContext, binding);

        var account = await _accountRepo.FindByIdAsync(binding.PartnerAccountId, cancellationToken);
        if (account is null)
            return Result.Failure<MarketplaceInboundEvent>(Error.NotFound("Marketplace.AccountNotFound", "Conta parceira do vínculo não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, account);

        var connector = _connectorRegistry.GetRequired(provider);
        var secret = _secretProtector.Unprotect(account.EncryptedCredentialsBlob);
        var signatureValid = connector.VerifyWebhookSignature(rawPayloadJson, signatureHeader, secret);

        if (!signatureValid && (connector.Capabilities.SupportsWebhookHmacSignature || !string.IsNullOrWhiteSpace(signatureHeader)))
        {
            return Result.Failure<MarketplaceInboundEvent>(Error.Unauthorized(
                "Marketplace.InvalidWebhookSignature",
                $"Credencial/assinatura inválida para webhook de {connector.Capabilities.DisplayName}."));
        }

        var utcNow = _clock.UtcNow;
        var normRes = connector.NormalizeOrderWebhook(rawPayloadJson, externalMerchantId);
        var eventRes = MarketplaceInboundEvent.Receive(
            tenantId,
            provider,
            binding.Id,
            externalEventId,
            normRes.IsSuccess ? normRes.Value.ExternalOrderId : externalEventId,
            externalMerchantId,
            eventType,
            rawPayloadJson,
            signatureValid,
            utcNow);

        if (eventRes.IsFailure)
            return eventRes;

        var inbound = eventRes.Value;
        if (normRes.IsSuccess)
        {
            var itemsJson = JsonSerializer.Serialize(normRes.Value.Items);
            inbound.MarkNormalized(normRes.Value, itemsJson, utcNow);
        }
        else
        {
            inbound.MarkFailed(normRes.Error.Message, utcNow);
        }

        await _eventRepo.AddAsync(inbound, cancellationToken);
        await _eventRepo.SaveChangesAsync(cancellationToken);

        return normRes.IsSuccess
            ? Result.Success(inbound)
            : Result.Failure<MarketplaceInboundEvent>(normRes.Error);
    }

    public async Task<Result<string>> ExecuteBindingActionAsync(
        User actor,
        Guid storeBindingId,
        MarketplaceActionKind action,
        string? targetExternalId = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        var reqPerm = action == MarketplaceActionKind.CancelOrder ? PermissionAction.Cancel : PermissionAction.Edit;
        var guard = ValidateActor(actor, reqPerm);
        if (guard.IsFailure)
            return Result.Failure<string>(guard.Error);

        var binding = await _bindingRepo.FindByIdAsync(storeBindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<string>(Error.NotFound("Marketplace.StoreBindingNotFound", "Vínculo de loja externa não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, binding);

        var account = await _accountRepo.FindByIdAsync(binding.PartnerAccountId, cancellationToken);
        if (account is null)
            return Result.Failure<string>(Error.NotFound("Marketplace.AccountNotFound", "Conta parceira não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, account);

        var connector = _connectorRegistry.GetRequired(binding.Provider);
        var res = await connector.ExecuteActionAsync(
            account,
            binding,
            action,
            targetExternalId,
            string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            _remoteGateway,
            cancellationToken);

        var utcNow = _clock.UtcNow;
        if (res.IsSuccess)
        {
            binding.Reconnect(utcNow);
        }
        else if (res.Error.Code is not "Marketplace.CapabilityUnsupported")
        {
            var isAuthErr = res.Error.Type == ErrorType.Unauthorized;
            binding.RecordSyncFailure(res.Error.Message, utcNow, tokenExpired: isAuthErr);
        }

        await _bindingRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de operar marketplaces de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Marketplaces, action))
            return Result.Failure<Guid>(Error.Forbidden("Marketplaces.Forbidden", "Permissão insuficiente para operar hub de marketplaces."));

        return Result.Success(tenantId);
    }
}
