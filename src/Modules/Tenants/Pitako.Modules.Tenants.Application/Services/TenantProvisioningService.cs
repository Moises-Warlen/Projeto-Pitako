using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Application.Services;

public sealed record CreateEstablishmentCommand(
    Guid CustomerAccountId,
    string LegalName,
    string TradeName,
    string Slug,
    string Cnpj,
    string SegmentCode,
    string? Address = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record ProvisionedEstablishmentResult(
    Tenant Tenant,
    StoreUnit HeadquartersUnit,
    StoreVisualIdentity VisualIdentity,
    BusinessSegmentProfile SegmentProfile);

/// <summary>
/// Serviço de provisionamento de estabelecimentos e unidades com validação de cota do plano no backend (Seções 6, 7 e 11).
/// </summary>
public sealed class TenantProvisioningService
{
    private readonly ITenantRepository _tenantRepo;
    private readonly IStoreUnitRepository _unitRepo;
    private readonly IStoreVisualIdentityRepository _identityRepo;
    private readonly IPlanQuotaPolicyChecker _quotaChecker;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantContextSetter _tenantContextSetter;

    public TenantProvisioningService(
        ITenantRepository tenantRepo,
        IStoreUnitRepository unitRepo,
        IStoreVisualIdentityRepository identityRepo,
        IPlanQuotaPolicyChecker quotaChecker,
        ITenantContext tenantContext,
        ITenantContextSetter tenantContextSetter)
    {
        _tenantRepo = tenantRepo;
        _unitRepo = unitRepo;
        _identityRepo = identityRepo;
        _quotaChecker = quotaChecker;
        _tenantContext = tenantContext;
        _tenantContextSetter = tenantContextSetter;
    }

    public async Task<Result<ProvisionedEstablishmentResult>> CreateEstablishmentAsync(
        User actor,
        CreateEstablishmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Scope != UserScope.Saas || !actor.HasPermission(SystemModules.Tenants, PermissionAction.Create))
            return Result.Failure<ProvisionedEstablishmentResult>(Error.Forbidden("Tenants.ProvisionForbidden", "Somente equipe SaaS autorizada pode provisionar novos estabelecimentos."));

        if (command.CustomerAccountId == Guid.Empty)
            return Result.Failure<ProvisionedEstablishmentResult>(Error.Validation("Tenants.CustomerRequired", "Conta cliente SaaS é obrigatória."));

        var currentEstablishments = await _tenantRepo.CountByCustomerAccountAsync(command.CustomerAccountId, cancellationToken);
        var quotaCheck = await _quotaChecker.CheckCanCreateEstablishmentAsync(command.CustomerAccountId, currentEstablishments, cancellationToken);
        if (quotaCheck.IsFailure)
            return Result.Failure<ProvisionedEstablishmentResult>(quotaCheck.Error);

        var currentUnits = await _unitRepo.CountByCustomerAccountAsync(command.CustomerAccountId, cancellationToken);
        var unitQuotaCheck = await _quotaChecker.CheckCanCreateUnitAsync(command.CustomerAccountId, currentUnits, cancellationToken);
        if (unitQuotaCheck.IsFailure)
            return Result.Failure<ProvisionedEstablishmentResult>(unitQuotaCheck.Error);

        var tenantResult = Tenant.Create(
            command.LegalName,
            command.TradeName,
            command.Slug,
            command.Cnpj,
            command.SegmentCode,
            TenantStatus.Active,
            command.CustomerAccountId,
            command.Address,
            command.ContactPhone,
            command.ContactEmail);

        if (tenantResult.IsFailure)
            return Result.Failure<ProvisionedEstablishmentResult>(tenantResult.Error);

        var tenant = tenantResult.Value;
        if (await _tenantRepo.FindBySlugAsync(tenant.Slug, cancellationToken) is not null)
            return Result.Failure<ProvisionedEstablishmentResult>(Error.Conflict("Tenants.SlugAlreadyInUse", "Já existe um estabelecimento com este slug."));

        var existingTenants = await _tenantRepo.ListAllAsync(cancellationToken);
        if (existingTenants.Any(t => t.Cnpj.Digits == tenant.Cnpj.Digits))
            return Result.Failure<ProvisionedEstablishmentResult>(Error.Conflict("Tenants.CnpjAlreadyInUse", "Já existe uma loja cadastrada com este CNPJ."));

        var trimmedTrade = tenant.TradeName.Trim();
        if (existingTenants.Any(t => t.TradeName.Trim().Equals(trimmedTrade, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure<ProvisionedEstablishmentResult>(Error.Conflict("Tenants.TradeNameAlreadyInUse", $"Já existe uma loja cadastrada com o nome '{trimmedTrade}'."));

        var segmentProfile = BusinessSegmentCatalog.Resolve(tenant.SegmentCode).Value;

        // Define escopo de contexto para persistir as entidades isoladas do novo estabelecimento.
        _tenantContextSetter.Clear();
        _tenantContextSetter.SetTenant(tenant.Id, TenantResolutionSource.BackgroundJob);

        var hqUnit = StoreUnit.Create(tenant.Id, $"{tenant.TradeName} - Matriz", "MATRIZ", true, tenant.Address, tenant.ContactPhone).Value;
        var identity = StoreVisualIdentity.CreateDefault(tenant.Id);

        await _tenantRepo.AddAsync(tenant, cancellationToken);
        await _unitRepo.AddAsync(hqUnit, cancellationToken);
        await _identityRepo.AddAsync(identity, cancellationToken);
        await _tenantRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new ProvisionedEstablishmentResult(tenant, hqUnit, identity, segmentProfile));
    }

    public async Task<Result<StoreUnit>> CreateStoreUnitAsync(
        User actor,
        Guid targetTenantId,
        string name,
        string code,
        string? address = null,
        string? phone = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        TenantGuard.EnsureOwnership(_tenantContext, targetTenantId);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != targetTenantId)
            throw new CrossTenantAccessException(actor.TenantId, targetTenantId, "Usuário de outro estabelecimento não pode criar unidades nesta loja.");

        if (!actor.HasPermission(SystemModules.Tenants, PermissionAction.Create))
            return Result.Failure<StoreUnit>(Error.Forbidden("Tenants.UnitCreateForbidden", "Permissão insuficiente para cadastrar unidades."));

        var tenant = await _tenantRepo.FindByIdAsync(targetTenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<StoreUnit>(Error.NotFound("Tenants.NotFound", "Estabelecimento não encontrado."));

        if (tenant.CustomerAccountId.HasValue)
        {
            var currentUnits = await _unitRepo.CountByCustomerAccountAsync(tenant.CustomerAccountId.Value, cancellationToken);
            var quota = await _quotaChecker.CheckCanCreateUnitAsync(tenant.CustomerAccountId.Value, currentUnits, cancellationToken);
            if (quota.IsFailure)
                return Result.Failure<StoreUnit>(quota.Error);
        }

        var unitResult = StoreUnit.Create(targetTenantId, name, code, false, address, phone);
        if (unitResult.IsFailure)
            return unitResult;

        await _unitRepo.AddAsync(unitResult.Value, cancellationToken);
        await _unitRepo.SaveChangesAsync(cancellationToken);
        return unitResult;
    }
}
