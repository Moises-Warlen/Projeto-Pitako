using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Marketplaces.Domain;

public enum MarketplaceBulkOperationType
{
    ToggleStoreOpenClose = 1,
    ToggleProductAvailability = 2,
    SyncProductPrice = 3
}

public enum MarketplaceBulkJobStatus
{
    Pending = 0,
    Completed = 1,
    PartialSuccess = 2,
    Failed = 3
}

public enum MarketplaceBulkItemOutcome
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    UnsupportedCapability = 3,
    SkippedMissingMapping = 4
}

/// <summary>
/// Resultado individual por loja vinculada dentro de uma execução em massa (Seção 28).
/// </summary>
public sealed class MarketplaceBulkJobItem : Entity<Guid>, ITenantScopedEntity
{
    private MarketplaceBulkJobItem()
    {
        ExternalStoreName = string.Empty;
        ExternalMerchantId = string.Empty;
    }

    internal MarketplaceBulkJobItem(
        Guid id,
        Guid tenantId,
        Guid bulkJobId,
        Guid storeBindingId,
        MarketplaceProvider provider,
        string externalMerchantId,
        string externalStoreName,
        string? targetSkuOrRef) : base(id)
    {
        TenantId = tenantId;
        BulkJobId = bulkJobId;
        StoreBindingId = storeBindingId;
        Provider = provider;
        ExternalMerchantId = externalMerchantId;
        ExternalStoreName = externalStoreName;
        TargetSkuOrRef = targetSkuOrRef;
        Outcome = MarketplaceBulkItemOutcome.Pending;
    }

    public Guid TenantId { get; private set; }
    public Guid BulkJobId { get; private set; }
    public Guid StoreBindingId { get; private set; }
    public MarketplaceProvider Provider { get; private set; }
    public string ExternalMerchantId { get; private set; }
    public string ExternalStoreName { get; private set; }
    public string? TargetSkuOrRef { get; private set; }
    public MarketplaceBulkItemOutcome Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }

    internal void RecordOutcome(
        MarketplaceBulkItemOutcome outcome,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset executedAtUtc)
    {
        AttemptCount++;
        Outcome = outcome;
        ErrorCode = outcome == MarketplaceBulkItemOutcome.Succeeded ? null : errorCode;
        ErrorMessage = outcome == MarketplaceBulkItemOutcome.Succeeded ? null : errorMessage;
        ExecutedAtUtc = executedAtUtc;
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em MarketplaceBulkJobItem.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Trabalho em lote multi-loja/multi-conector que nunca reporta sucesso global diante de falhas parciais (Seção 28).
/// </summary>
public sealed class MarketplaceBulkJob : TenantScopedAggregateRoot
{
    private readonly List<MarketplaceBulkJobItem> _items = new();

    private MarketplaceBulkJob()
    {
        OperationSummary = string.Empty;
    }

    private MarketplaceBulkJob(
        Guid id,
        Guid tenantId,
        MarketplaceBulkOperationType operationType,
        string operationSummary,
        Guid? internalProductId,
        decimal? targetPriceAmount,
        bool? targetPausedState,
        Guid requestedByUserId,
        DateTimeOffset startedAtUtc) : base(id, tenantId)
    {
        OperationType = operationType;
        OperationSummary = operationSummary;
        InternalProductId = internalProductId;
        TargetPriceAmount = targetPriceAmount;
        TargetPausedState = targetPausedState;
        RequestedByUserId = requestedByUserId;
        Status = MarketplaceBulkJobStatus.Pending;
        StartedAtUtc = startedAtUtc;
    }

    public MarketplaceBulkOperationType OperationType { get; private set; }
    public string OperationSummary { get; private set; }
    public Guid? InternalProductId { get; private set; }
    public decimal? TargetPriceAmount { get; private set; }
    public bool? TargetPausedState { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public MarketplaceBulkJobStatus Status { get; private set; }
    public int TotalTargets { get; private set; }
    public int SucceededCount { get; private set; }
    public int FailedCount { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsGlobalSuccess =>
        Status == MarketplaceBulkJobStatus.Completed && FailedCount == 0 && SucceededCount == TotalTargets;

    public IReadOnlyCollection<MarketplaceBulkJobItem> Items => _items.AsReadOnly();

    public static Result<MarketplaceBulkJob> Start(
        Guid tenantId,
        MarketplaceBulkOperationType operationType,
        string operationSummary,
        IReadOnlyList<MarketplaceStoreBinding> targets,
        Guid requestedByUserId,
        DateTimeOffset startedAtUtc,
        Guid? internalProductId = null,
        decimal? targetPriceAmount = null,
        bool? targetPausedState = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("BulkJob.TenantRequired", "TenantId é obrigatório."));
        if (requestedByUserId == Guid.Empty)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("BulkJob.UserRequired", "Operador responsável é obrigatório."));
        if (targets is null || targets.Count == 0)
            return Result.Failure<MarketplaceBulkJob>(Error.Validation("BulkJob.NoTargets", "Selecione ao menos uma loja vinculada para ação em massa."));
        if (targets.Any(t => t.TenantId != tenantId))
            return Result.Failure<MarketplaceBulkJob>(Error.Forbidden("BulkJob.CrossTenantTarget", "Uma ou mais lojas pertencem a outro estabelecimento."));

        var job = new MarketplaceBulkJob(
            Guid.NewGuid(),
            tenantId,
            operationType,
            string.IsNullOrWhiteSpace(operationSummary) ? operationType.ToString() : operationSummary.Trim(),
            internalProductId,
            targetPriceAmount,
            targetPausedState,
            requestedByUserId,
            startedAtUtc);

        foreach (var target in targets.DistinctBy(t => t.Id))
        {
            job._items.Add(new MarketplaceBulkJobItem(
                Guid.NewGuid(),
                tenantId,
                job.Id,
                target.Id,
                target.Provider,
                target.ExternalMerchantId,
                target.ExternalStoreName,
                null));
        }

        job.RecalculateSummary(startedAtUtc);
        return Result.Success(job);
    }

    public void RecordItemResult(
        Guid storeBindingId,
        MarketplaceBulkItemOutcome outcome,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset executedAtUtc)
    {
        var item = _items.FirstOrDefault(i => i.StoreBindingId == storeBindingId)
                   ?? throw new InvalidOperationException($"Loja {storeBindingId} não pertence ao lote {Id}.");

        item.RecordOutcome(outcome, errorCode, errorMessage, executedAtUtc);
        RecalculateSummary(executedAtUtc);
    }

    private void RecalculateSummary(DateTimeOffset utcNow)
    {
        TotalTargets = _items.Count;
        SucceededCount = _items.Count(i => i.Outcome == MarketplaceBulkItemOutcome.Succeeded);
        FailedCount = _items.Count(i => i.Outcome is MarketplaceBulkItemOutcome.Failed
            or MarketplaceBulkItemOutcome.UnsupportedCapability
            or MarketplaceBulkItemOutcome.SkippedMissingMapping);

        var anyPending = _items.Any(i => i.Outcome == MarketplaceBulkItemOutcome.Pending);
        if (anyPending)
        {
            Status = MarketplaceBulkJobStatus.Pending;
            return;
        }

        CompletedAtUtc = utcNow;
        Status = FailedCount == 0
            ? MarketplaceBulkJobStatus.Completed
            : SucceededCount > 0
                ? MarketplaceBulkJobStatus.PartialSuccess
                : MarketplaceBulkJobStatus.Failed;
    }
}
