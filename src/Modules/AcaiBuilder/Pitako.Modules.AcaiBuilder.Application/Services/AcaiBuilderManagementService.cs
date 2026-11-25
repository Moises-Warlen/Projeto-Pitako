using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.AcaiBuilder.Application.Services;

/// <summary>
/// Catálogo separado para montagem de açaí: tamanhos de copo, sabores base,
/// acompanhamentos gratuitos e área exclusiva de adicionais pagos (Seção 13).
/// </summary>
public sealed record AcaiMenuCatalogView(
    IReadOnlyList<AcaiCupSize> CupSizes,
    IReadOnlyList<AcaiBaseFlavor> BaseFlavors,
    IReadOnlyList<AcaiFreeTopping> FreeToppings,
    IReadOnlyList<AcaiPaidAddon> PaidAddons);

/// <summary>
/// Caso de uso de administração e montagem autoritativa de copos personalizados de açaí (Seção 13).
/// </summary>
public sealed class AcaiBuilderManagementService : IAcaiAssemblyPricingService
{
    private readonly IAcaiCupSizeRepository _cupRepo;
    private readonly IAcaiBaseFlavorRepository _flavorRepo;
    private readonly IAcaiFreeToppingRepository _toppingRepo;
    private readonly IAcaiPaidAddonRepository _addonRepo;
    private readonly ITenantContext _tenantContext;

    public AcaiBuilderManagementService(
        IAcaiCupSizeRepository cupRepo,
        IAcaiBaseFlavorRepository flavorRepo,
        IAcaiFreeToppingRepository toppingRepo,
        IAcaiPaidAddonRepository addonRepo,
        ITenantContext tenantContext)
    {
        _cupRepo = cupRepo;
        _flavorRepo = flavorRepo;
        _toppingRepo = toppingRepo;
        _addonRepo = addonRepo;
        _tenantContext = tenantContext;
    }

    public async Task<Result<AcaiCupSize>> CreateCupSizeAsync(
        User actor,
        string name,
        int volumeMl,
        decimal basePriceAmount,
        int freeToppingsLimit,
        int minFreeToppings = 0,
        AcaiFreeCountingMode countingMode = AcaiFreeCountingMode.ByPortion,
        int? maxPaidAddonsPerCup = null,
        Guid? linkedProductId = null,
        string? sku = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiCupSize>(guard.Error);

        var priceRes = Money.Create(basePriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<AcaiCupSize>(priceRes.Error);

        var cupRes = AcaiCupSize.Create(
            guard.Value,
            name,
            volumeMl,
            priceRes.Value,
            freeToppingsLimit,
            minFreeToppings,
            countingMode,
            maxPaidAddonsPerCup,
            linkedProductId,
            sku,
            displayOrder);

        if (cupRes.IsFailure)
            return cupRes;

        await _cupRepo.AddAsync(cupRes.Value, cancellationToken);
        await _cupRepo.SaveChangesAsync(cancellationToken);
        return cupRes;
    }

    public async Task<Result<AcaiBaseFlavor>> CreateBaseFlavorAsync(
        User actor,
        string name,
        decimal extraPriceAmount = 0m,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiBaseFlavor>(guard.Error);

        var extraRes = Money.Create(extraPriceAmount);
        if (extraRes.IsFailure)
            return Result.Failure<AcaiBaseFlavor>(extraRes.Error);

        var flavorRes = AcaiBaseFlavor.Create(guard.Value, name, extraRes.Value, displayOrder);
        if (flavorRes.IsFailure)
            return flavorRes;

        await _flavorRepo.AddAsync(flavorRes.Value, cancellationToken);
        await _flavorRepo.SaveChangesAsync(cancellationToken);
        return flavorRes;
    }

    public async Task<Result<AcaiFreeTopping>> CreateFreeToppingAsync(
        User actor,
        string name,
        int maxPortionsPerCup = 1,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerPortion = null,
        string? consumptionUnitCode = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiFreeTopping>(guard.Error);

        var toppingRes = AcaiFreeTopping.Create(
            guard.Value,
            name,
            maxPortionsPerCup,
            displayOrder,
            supplyItemId,
            consumptionQuantityPerPortion,
            consumptionUnitCode);

        if (toppingRes.IsFailure)
            return toppingRes;

        await _toppingRepo.AddAsync(toppingRes.Value, cancellationToken);
        await _toppingRepo.SaveChangesAsync(cancellationToken);
        return toppingRes;
    }

    public async Task<Result<AcaiPaidAddon>> CreatePaidAddonAsync(
        User actor,
        string name,
        decimal additionalPriceAmount,
        int maxQuantityPerCup = 3,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerUnit = null,
        string? consumptionUnitCode = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiPaidAddon>(guard.Error);

        var priceRes = Money.Create(additionalPriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<AcaiPaidAddon>(priceRes.Error);

        var addonRes = AcaiPaidAddon.Create(
            guard.Value,
            name,
            priceRes.Value,
            maxQuantityPerCup,
            displayOrder,
            supplyItemId,
            consumptionQuantityPerUnit,
            consumptionUnitCode);

        if (addonRes.IsFailure)
            return addonRes;

        await _addonRepo.AddAsync(addonRes.Value, cancellationToken);
        await _addonRepo.SaveChangesAsync(cancellationToken);
        return addonRes;
    }

    public async Task<Result> PauseFreeToppingAsync(User actor, Guid toppingId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var topping = await _toppingRepo.FindByIdAsync(toppingId, cancellationToken);
        if (topping is null)
            return Result.Failure(Error.NotFound("Acai.ToppingNotFound", "Acompanhamento gratuito não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, topping);
        topping.Pause();
        await _toppingRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PausePaidAddonAsync(User actor, Guid addonId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return guard;

        var addon = await _addonRepo.FindByIdAsync(addonId, cancellationToken);
        if (addon is null)
            return Result.Failure(Error.NotFound("Acai.AddonNotFound", "Adicional pago não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, addon);
        addon.Pause();
        await _addonRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<AcaiMenuCatalogView> GetMenuCatalogAsync(
        bool includePaused = false,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);

        var cups = await _cupRepo.ListAsync(includePaused, cancellationToken);
        var flavors = await _flavorRepo.ListAsync(includePaused, cancellationToken);
        var free = await _toppingRepo.ListAsync(includePaused, cancellationToken);
        var paid = await _addonRepo.ListAsync(includePaused, cancellationToken);

        return new AcaiMenuCatalogView(cups, flavors, free, paid);
    }

    public async Task<Result<AcaiCupAssemblySummary>> BuildAndPriceCupAsync(
        AcaiCupBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        var cup = await _cupRepo.FindByIdAsync(request.CupSizeId, cancellationToken);
        if (cup is null)
            return Result.Failure<AcaiCupAssemblySummary>(Error.NotFound("Acai.CupSizeUnavailable", "Tamanho de copo indisponível."));

        TenantGuard.EnsureOwnership(_tenantContext, cup);

        var flavorIds = (request.BaseFlavorIds ?? []).Distinct().ToList();
        var freeIds = (request.FreeToppings ?? []).Select(t => t.ItemId).Distinct().ToList();
        var paidIds = (request.PaidAddons ?? []).Select(a => a.ItemId).Distinct().ToList();

        var flavors = await _flavorRepo.FindLookupByIdsAsync(flavorIds, cancellationToken);
        var freeToppings = await _toppingRepo.FindLookupByIdsAsync(freeIds, cancellationToken);
        var paidAddons = await _addonRepo.FindLookupByIdsAsync(paidIds, cancellationToken);

        return AcaiCupAssemblyCalculator.ValidateAndBuild(
            tenantId,
            cup,
            flavors,
            freeToppings,
            paidAddons,
            request);
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de configurar módulo de açaí de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Acai, action))
            return Result.Failure<Guid>(Error.Forbidden("Acai.Forbidden", "Permissão insuficiente para configurar o módulo de açaí."));

        return Result.Success(currentTenantId);
    }
}
