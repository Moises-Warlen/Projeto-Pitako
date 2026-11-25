using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.MasterPanel.Domain;

public enum BillingCycle
{
    Monthly = 1,
    Quarterly = 2,
    Semiannual = 3,
    Annual = 4
}

public enum QuotaResourceKind
{
    Establishments = 1,
    Units = 2,
    MarketplaceStores = 3,
    Users = 4,
    Devices = 5,
    ConcurrentSessions = 6,
    MonthlyOrders = 7
}

/// <summary>
/// Conjunto configurável de limites por recurso de um plano comercial.
/// Dimensões configuradas via <see cref="QuotaLimit.Unlimited"/> removem o limite comercial sem teto artificial.
/// </summary>
public sealed record PlanQuotaConfiguration(
    QuotaLimit EstablishmentsLimit,
    QuotaLimit UnitsLimit,
    QuotaLimit MarketplaceStoresLimit,
    QuotaLimit UsersLimit,
    QuotaLimit DevicesLimit,
    QuotaLimit ConcurrentSessionsLimit,
    QuotaLimit MonthlyOrdersLimit);

public sealed class PlanModuleEntitlement : Entity<Guid>
{
    private PlanModuleEntitlement()
    {
        ModuleCode = string.Empty;
    }

    internal PlanModuleEntitlement(Guid id, Guid planId, string moduleCode) : base(id)
    {
        PlanId = planId;
        ModuleCode = NormalizeCode(moduleCode);
    }

    public Guid PlanId { get; private set; }
    public string ModuleCode { get; private set; }

    internal static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToLowerInvariant();
}

public sealed class PlanFeatureEntitlement : Entity<Guid>
{
    private PlanFeatureEntitlement()
    {
        FeatureCode = string.Empty;
    }

    internal PlanFeatureEntitlement(Guid id, Guid planId, string featureCode) : base(id)
    {
        PlanId = planId;
        FeatureCode = PlanModuleEntitlement.NormalizeCode(featureCode);
    }

    public Guid PlanId { get; private set; }
    public string FeatureCode { get; private set; }
}

/// <summary>
/// Plano comercial configurável pelo administrador Master (Seção 7).
/// Nenhum recurso fica preso a um plano fixo; módulos e cotas são dinâmicos.
/// </summary>
public sealed class CommercialPlan : AggregateRoot<Guid>, IAuditableEntity
{
    private readonly List<PlanModuleEntitlement> _modules = new();
    private readonly List<PlanFeatureEntitlement> _features = new();

    private CommercialPlan()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
        Price = Money.Zero();
        EstablishmentsLimit = QuotaLimit.Limited(1).Value;
        UnitsLimit = QuotaLimit.Limited(1).Value;
        MarketplaceStoresLimit = QuotaLimit.Limited(0).Value;
        UsersLimit = QuotaLimit.Limited(1).Value;
        DevicesLimit = QuotaLimit.Limited(1).Value;
        ConcurrentSessionsLimit = QuotaLimit.Limited(1).Value;
        MonthlyOrdersLimit = QuotaLimit.Unlimited();
    }

    private CommercialPlan(
        Guid id,
        string code,
        string name,
        string description,
        Money price,
        BillingCycle billingCycle,
        int trialDays,
        bool isPublic,
        PlanQuotaConfiguration quotas) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        Price = price;
        BillingCycle = billingCycle;
        TrialDays = trialDays;
        IsPublic = isPublic;
        IsActive = true;
        EstablishmentsLimit = quotas.EstablishmentsLimit;
        UnitsLimit = quotas.UnitsLimit;
        MarketplaceStoresLimit = quotas.MarketplaceStoresLimit;
        UsersLimit = quotas.UsersLimit;
        DevicesLimit = quotas.DevicesLimit;
        ConcurrentSessionsLimit = quotas.ConcurrentSessionsLimit;
        MonthlyOrdersLimit = quotas.MonthlyOrdersLimit;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public Money Price { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public int TrialDays { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPublic { get; private set; }

    public QuotaLimit EstablishmentsLimit { get; private set; }
    public QuotaLimit UnitsLimit { get; private set; }
    public QuotaLimit MarketplaceStoresLimit { get; private set; }
    public QuotaLimit UsersLimit { get; private set; }
    public QuotaLimit DevicesLimit { get; private set; }
    public QuotaLimit ConcurrentSessionsLimit { get; private set; }
    public QuotaLimit MonthlyOrdersLimit { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<PlanModuleEntitlement> Modules => _modules.AsReadOnly();
    public IReadOnlyCollection<PlanFeatureEntitlement> Features => _features.AsReadOnly();

    public static Result<CommercialPlan> Create(
        string code,
        string name,
        string description,
        Money price,
        BillingCycle billingCycle,
        int trialDays,
        PlanQuotaConfiguration quotas,
        IEnumerable<string> enabledModules,
        IEnumerable<string>? enabledFeatures = null,
        bool isPublic = true)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<CommercialPlan>(Error.Validation("Plan.CodeRequired", "Código do plano é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<CommercialPlan>(Error.Validation("Plan.NameRequired", "Nome do plano é obrigatório."));

        if (trialDays < 0)
            return Result.Failure<CommercialPlan>(Error.Validation("Plan.InvalidTrialDays", "Período de teste não pode ser negativo."));

        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quotas);

        var plan = new CommercialPlan(
            Guid.NewGuid(),
            code.Trim().ToLowerInvariant(),
            name.Trim(),
            description?.Trim() ?? string.Empty,
            price,
            billingCycle,
            trialDays,
            isPublic,
            quotas);

        plan.SetModules(enabledModules ?? []);
        plan.SetFeatures(enabledFeatures ?? []);
        return Result.Success(plan);
    }

    public Result UpdateConfiguration(
        string name,
        string description,
        Money price,
        BillingCycle billingCycle,
        int trialDays,
        bool isPublic,
        PlanQuotaConfiguration quotas,
        IEnumerable<string> enabledModules,
        IEnumerable<string>? enabledFeatures = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Plan.NameRequired", "Nome do plano é obrigatório."));

        if (trialDays < 0)
            return Result.Failure(Error.Validation("Plan.InvalidTrialDays", "Período de teste não pode ser negativo."));

        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quotas);

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Price = price;
        BillingCycle = billingCycle;
        TrialDays = trialDays;
        IsPublic = isPublic;
        ApplyQuotas(quotas);
        SetModules(enabledModules);
        SetFeatures(enabledFeatures ?? []);
        return Result.Success();
    }

    public QuotaLimit GetLimit(QuotaResourceKind kind) => kind switch
    {
        QuotaResourceKind.Establishments => EstablishmentsLimit,
        QuotaResourceKind.Units => UnitsLimit,
        QuotaResourceKind.MarketplaceStores => MarketplaceStoresLimit,
        QuotaResourceKind.Users => UsersLimit,
        QuotaResourceKind.Devices => DevicesLimit,
        QuotaResourceKind.ConcurrentSessions => ConcurrentSessionsLimit,
        QuotaResourceKind.MonthlyOrders => MonthlyOrdersLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public bool IsModuleEnabled(string moduleCode)
    {
        var normalized = PlanModuleEntitlement.NormalizeCode(moduleCode);
        return _modules.Any(m => m.ModuleCode == normalized);
    }

    public bool IsFeatureEnabled(string featureCode)
    {
        var normalized = PlanModuleEntitlement.NormalizeCode(featureCode);
        return _features.Any(f => f.FeatureCode == normalized);
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    private void ApplyQuotas(PlanQuotaConfiguration quotas)
    {
        EstablishmentsLimit = quotas.EstablishmentsLimit;
        UnitsLimit = quotas.UnitsLimit;
        MarketplaceStoresLimit = quotas.MarketplaceStoresLimit;
        UsersLimit = quotas.UsersLimit;
        DevicesLimit = quotas.DevicesLimit;
        ConcurrentSessionsLimit = quotas.ConcurrentSessionsLimit;
        MonthlyOrdersLimit = quotas.MonthlyOrdersLimit;
    }

    private void SetModules(IEnumerable<string> moduleCodes)
    {
        _modules.Clear();
        foreach (var code in moduleCodes.Select(PlanModuleEntitlement.NormalizeCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            _modules.Add(new PlanModuleEntitlement(Guid.NewGuid(), Id, code));
        }
    }

    private void SetFeatures(IEnumerable<string> featureCodes)
    {
        _features.Clear();
        foreach (var code in featureCodes.Select(PlanModuleEntitlement.NormalizeCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            _features.Add(new PlanFeatureEntitlement(Guid.NewGuid(), Id, code));
        }
    }

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}
