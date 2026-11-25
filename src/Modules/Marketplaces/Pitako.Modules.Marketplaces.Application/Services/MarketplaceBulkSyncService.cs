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

public sealed record MultiStoreHealthSummary(
    int TotalStores,
    int ConnectedCount,
    int PausedCount,
    int ErrorOrExpiredCount,
    IReadOnlyList<MarketplaceStoreBinding> Bindings);

/// <summary>
/// Orquestração multi-loja de marketplaces, ações em massa com relatório parcial honesto
/// e reprocessamento de falhas respeitando as capacidades reais de cada conector (Seção 28).
/// </summary>
public sealed class MarketplaceBulkSyncService
{
    private readonly IMarketplacePartnerAccountRepository _accountRepo;
    private readonly IMarketplaceStoreBindingRepository _bindingRepo;
    private readonly IMarketplaceCatalogMappingRepository _mappingRepo;
    private readonly IMarketplaceBulkJobRepository _bulkRepo;
    private readonly MarketplaceConnectorRegistry _connectorRegistry;
    private readonly IMarketplaceRemoteGateway _remoteGateway;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public MarketplaceBulkSyncService(
        IMarketplacePartnerAccountRepository accountRepo,
        IMarketplaceStoreBindingRepository bindingRepo,
        IMarketplaceCatalogMappingRepository mappingRepo,
        IMarketplaceBulkJobRepository bulkRepo,
        MarketplaceConnectorRegistry connectorRegistry,
        IMarketplaceRemoteGateway remoteGateway,
        ITenantContext tenantContext,
        IClock clock)
    {
        _accountRepo = accountRepo;
        _bindingRepo = bindingRepo;
        _mappingRepo = mappingRepo;
        _bulkRepo = bulkRepo;
        _connectorRegistry = connectorRegistry;
        _remoteGateway = remoteGateway;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<MultiStoreHealthSummary>> GetMultiStoreHealthDashboardAsync(
        User actor,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<MultiStoreHealthSummary>(guard.Error);

        var bindings = await _bindingRepo.ListAllAsync(cancellationToken);
        return Result.Success(new MultiStoreHealthSummary(
            bindings.Count,
            bindings.Count(b => b.Status == MarketplaceBindingStatus.Connected),
            bindings.Count(b => b.Status == MarketplaceBindingStatus.Paused),
            bindings.Count(b => b.Status is MarketplaceBindingStatus.Error or MarketplaceBindingStatus.TokenExpired),
            bindings));
    }

    public async Task<Result<MarketplaceBulkJob>> BulkToggleStoresOpenCloseAsync(
        User actor,
        IReadOnlyList<Guid> storeBindingIds,
        bool pauseStore,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(guard.Error);

        var loadRes = await LoadAndValidateTargetsAsync(storeBindingIds, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(loadRes.Error);

        var bindings = loadRes.Value;
        var summary = pauseStore ? $"Pausar {bindings.Count} loja(s) em lote" : $"Reabrir {bindings.Count} loja(s) em lote";
        var jobRes = MarketplaceBulkJob.Start(
            guard.Value,
            MarketplaceBulkOperationType.ToggleStoreOpenClose,
            summary,
            bindings,
            actor.Id,
            _clock.UtcNow,
            targetPausedState: pauseStore);

        if (jobRes.IsFailure)
            return jobRes;

        var job = jobRes.Value;
        await ExecuteTargetsAsync(job, bindings, mappingsByBinding: null, reason: reason, retryOnlyFailed: false, cancellationToken);

        await _bulkRepo.AddAsync(job, cancellationToken);
        await _bulkRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    public async Task<Result<MarketplaceBulkJob>> BulkToggleProductAvailabilityAsync(
        User actor,
        Guid internalProductId,
        bool isPaused,
        IReadOnlyList<Guid> storeBindingIds,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(guard.Error);
        if (internalProductId == Guid.Empty)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("Marketplace.ProductRequired", "Produto interno é obrigatório."));

        var loadRes = await LoadAndValidateTargetsAsync(storeBindingIds, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(loadRes.Error);

        var bindings = loadRes.Value;
        var mappings = (await _mappingRepo.ListByInternalProductAsync(internalProductId, cancellationToken))
            .ToDictionary(m => m.StoreBindingId);

        var summary = isPaused
            ? $"Pausar SKU em {bindings.Count} loja(s)"
            : $"Reativar SKU em {bindings.Count} loja(s)";

        var jobRes = MarketplaceBulkJob.Start(
            guard.Value,
            MarketplaceBulkOperationType.ToggleProductAvailability,
            summary,
            bindings,
            actor.Id,
            _clock.UtcNow,
            internalProductId: internalProductId,
            targetPausedState: isPaused);

        if (jobRes.IsFailure)
            return jobRes;

        var job = jobRes.Value;
        await ExecuteTargetsAsync(job, bindings, mappings, reason: null, retryOnlyFailed: false, cancellationToken);

        await _bulkRepo.AddAsync(job, cancellationToken);
        await _bulkRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    public async Task<Result<MarketplaceBulkJob>> BulkSyncProductPriceAsync(
        User actor,
        Guid internalProductId,
        decimal newPriceAmount,
        IReadOnlyList<Guid> storeBindingIds,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(guard.Error);
        if (internalProductId == Guid.Empty)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("Marketplace.ProductRequired", "Produto interno é obrigatório."));

        var moneyRes = Money.Create(newPriceAmount);
        if (moneyRes.IsFailure || newPriceAmount <= 0m)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("Marketplace.InvalidPrice", "Preço de sincronização deve ser positivo."));

        var loadRes = await LoadAndValidateTargetsAsync(storeBindingIds, cancellationToken);
        if (loadRes.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(loadRes.Error);

        var bindings = loadRes.Value;
        var mappings = (await _mappingRepo.ListByInternalProductAsync(internalProductId, cancellationToken))
            .ToDictionary(m => m.StoreBindingId);

        var jobRes = MarketplaceBulkJob.Start(
            guard.Value,
            MarketplaceBulkOperationType.SyncProductPrice,
            $"Atualizar preço para {moneyRes.Value} em {bindings.Count} loja(s)",
            bindings,
            actor.Id,
            _clock.UtcNow,
            internalProductId: internalProductId,
            targetPriceAmount: moneyRes.Value.Amount);

        if (jobRes.IsFailure)
            return jobRes;

        var job = jobRes.Value;
        await ExecuteTargetsAsync(job, bindings, mappings, reason: null, retryOnlyFailed: false, cancellationToken);

        await _bulkRepo.AddAsync(job, cancellationToken);
        await _bulkRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    public async Task<Result<MarketplaceBulkJob>> RetryFailedItemsAsync(
        User actor,
        Guid bulkJobId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<MarketplaceBulkJob>(guard.Error);

        var job = await _bulkRepo.FindByIdAsync(bulkJobId, cancellationToken);
        if (job is null)
            return Result.Failure<MarketplaceBulkJob>(Error.NotFound("BulkJob.NotFound", "Execução em massa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, job);

        var bindingIds = job.Items.Select(i => i.StoreBindingId).ToList();
        var bindings = await _bindingRepo.ListByIdsAsync(bindingIds, cancellationToken);

        Dictionary<Guid, MarketplaceCatalogMapping>? mappings = null;
        if (job.InternalProductId.HasValue)
        {
            mappings = (await _mappingRepo.ListByInternalProductAsync(job.InternalProductId.Value, cancellationToken))
                .ToDictionary(m => m.StoreBindingId);
        }

        await ExecuteTargetsAsync(job, bindings, mappings, reason: null, retryOnlyFailed: true, cancellationToken);
        await _bulkRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(job);
    }

    private async Task ExecuteTargetsAsync(
        MarketplaceBulkJob job,
        IReadOnlyList<MarketplaceStoreBinding> bindings,
        IReadOnlyDictionary<Guid, MarketplaceCatalogMapping>? mappingsByBinding,
        string? reason,
        bool retryOnlyFailed,
        CancellationToken cancellationToken)
    {
        var bindingMap = bindings.ToDictionary(b => b.Id);
        var accountCache = new Dictionary<Guid, MarketplacePartnerAccount>();

        foreach (var item in job.Items.ToList())
        {
            if (retryOnlyFailed && item.Outcome is MarketplaceBulkItemOutcome.Succeeded or MarketplaceBulkItemOutcome.UnsupportedCapability)
                continue;

            var utcNow = _clock.UtcNow;
            if (!bindingMap.TryGetValue(item.StoreBindingId, out var binding))
            {
                job.RecordItemResult(item.StoreBindingId, MarketplaceBulkItemOutcome.Failed, "Marketplace.StoreBindingNotFound", "Vínculo removido.", utcNow);
                continue;
            }

            if (!accountCache.TryGetValue(binding.PartnerAccountId, out var account))
            {
                var acc = await _accountRepo.FindByIdAsync(binding.PartnerAccountId, cancellationToken);
                if (acc is null)
                {
                    job.RecordItemResult(binding.Id, MarketplaceBulkItemOutcome.Failed, "Marketplace.AccountNotFound", "Conta parceira não encontrada.", utcNow);
                    continue;
                }
                TenantGuard.EnsureOwnership(_tenantContext, acc);
                accountCache[acc.Id] = account = acc;
            }

            var connector = _connectorRegistry.GetRequired(binding.Provider);
            var actionKind = job.OperationType switch
            {
                MarketplaceBulkOperationType.ToggleStoreOpenClose => MarketplaceActionKind.ToggleStoreOpenClose,
                MarketplaceBulkOperationType.ToggleProductAvailability => MarketplaceActionKind.ToggleItemAvailability,
                _ => MarketplaceActionKind.SyncItemPrice
            };

            if (!connector.Capabilities.SupportsAction(actionKind))
            {
                job.RecordItemResult(
                    binding.Id,
                    MarketplaceBulkItemOutcome.UnsupportedCapability,
                    "Marketplace.CapabilityUnsupported",
                    $"Provedor {connector.Capabilities.DisplayName} não suporta {actionKind}.",
                    utcNow);
                continue;
            }

            string? targetExternalId = null;
            string payloadJson;
            MarketplaceCatalogMapping? mapping = null;

            if (job.OperationType == MarketplaceBulkOperationType.ToggleStoreOpenClose)
            {
                payloadJson = JsonSerializer.Serialize(new { paused = job.TargetPausedState ?? true, reason });
            }
            else
            {
                if (mappingsByBinding is null || !mappingsByBinding.TryGetValue(binding.Id, out mapping))
                {
                    job.RecordItemResult(
                        binding.Id,
                        MarketplaceBulkItemOutcome.SkippedMissingMapping,
                        "Marketplace.SkuMappingMissing",
                        $"Produto sem SKU mapeado na loja '{binding.ExternalStoreName}'.",
                        utcNow);
                    continue;
                }

                targetExternalId = mapping.ExternalSkuCode;
                payloadJson = job.OperationType == MarketplaceBulkOperationType.ToggleProductAvailability
                    ? JsonSerializer.Serialize(new { sku = mapping.ExternalSkuCode, paused = job.TargetPausedState ?? true })
                    : JsonSerializer.Serialize(new { sku = mapping.ExternalSkuCode, price = job.TargetPriceAmount ?? 0m });
            }

            var execRes = await connector.ExecuteActionAsync(account, binding, actionKind, targetExternalId, payloadJson, _remoteGateway, cancellationToken);
            if (execRes.IsFailure)
            {
                binding.RecordSyncFailure(execRes.Error.Message, utcNow, execRes.Error.Type == ErrorType.Unauthorized);
                job.RecordItemResult(binding.Id, MarketplaceBulkItemOutcome.Failed, execRes.Error.Code, execRes.Error.Message, utcNow);
                continue;
            }

            binding.Reconnect(utcNow);
            if (job.OperationType == MarketplaceBulkOperationType.ToggleStoreOpenClose)
            {
                if (job.TargetPausedState == true)
                    binding.Pause(reason);
                else
                    binding.Reconnect(utcNow);
            }
            else if (mapping is not null)
            {
                if (job.OperationType == MarketplaceBulkOperationType.ToggleProductAvailability)
                    mapping.RecordAvailabilitySync(job.TargetPausedState ?? true, utcNow);
                else if (job.TargetPriceAmount.HasValue)
                    mapping.RecordPriceSync(Money.Create(job.TargetPriceAmount.Value).Value, utcNow);
            }

            job.RecordItemResult(binding.Id, MarketplaceBulkItemOutcome.Succeeded, null, null, utcNow);
        }
    }

    private async Task<Result<IReadOnlyList<MarketplaceStoreBinding>>> LoadAndValidateTargetsAsync(
        IReadOnlyList<Guid> storeBindingIds,
        CancellationToken cancellationToken)
    {
        if (storeBindingIds is null || storeBindingIds.Count == 0)
            return Result.Failure<IReadOnlyList<MarketplaceStoreBinding>>(Error.Validation("BulkJob.NoTargets", "Selecione ao menos uma loja vinculada."));

        var distinctIds = storeBindingIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
            return Result.Failure<IReadOnlyList<MarketplaceStoreBinding>>(Error.Validation("BulkJob.NoTargets", "Nenhuma loja válida informada."));

        var bindings = await _bindingRepo.ListByIdsAsync(distinctIds, cancellationToken);
        if (bindings.Count != distinctIds.Count)
            return Result.Failure<IReadOnlyList<MarketplaceStoreBinding>>(Error.NotFound("Marketplace.StoreBindingNotFound", "Uma ou mais lojas selecionadas não foram encontradas para este estabelecimento."));

        foreach (var b in bindings)
            TenantGuard.EnsureOwnership(_tenantContext, b);

        return Result.Success(bindings);
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de executar ação em massa de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Marketplaces, action))
            return Result.Failure<Guid>(Error.Forbidden("Marketplaces.Forbidden", "Permissão insuficiente para ações em massa nos marketplaces."));

        return Result.Success(tenantId);
    }
}
