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
    IReadOnlyList<AcaiPaidAddon> PaidAddons,
    IReadOnlyList<AcaiAddonCategory>? AddonCategories = null);

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
    private readonly IAcaiAddonCategoryRepository? _addonCategoryRepo;

    public AcaiBuilderManagementService(
        IAcaiCupSizeRepository cupRepo,
        IAcaiBaseFlavorRepository flavorRepo,
        IAcaiFreeToppingRepository toppingRepo,
        IAcaiPaidAddonRepository addonRepo,
        ITenantContext tenantContext,
        IAcaiAddonCategoryRepository? addonCategoryRepo = null)
    {
        _cupRepo = cupRepo;
        _flavorRepo = flavorRepo;
        _toppingRepo = toppingRepo;
        _addonRepo = addonRepo;
        _tenantContext = tenantContext;
        _addonCategoryRepo = addonCategoryRepo;
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
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal costPriceAmount = 0m,
        bool allowFreeToppings = true,
        bool allowPaidAddons = true)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiCupSize>(guard.Error);

        var priceRes = Money.Create(basePriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<AcaiCupSize>(priceRes.Error);

        var costRes = Money.Create(Math.Max(0m, costPriceAmount));
        if (costRes.IsFailure)
            return Result.Failure<AcaiCupSize>(costRes.Error);

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
            displayOrder,
            description,
            costRes.Value,
            allowFreeToppings,
            allowPaidAddons);

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
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal costPriceAmount = 0m,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiFreeTopping>(guard.Error);

        var costRes = Money.Create(Math.Max(0m, costPriceAmount));
        if (costRes.IsFailure)
            return Result.Failure<AcaiFreeTopping>(costRes.Error);

        var resolvedCategoryName = await ResolveAddonCategoryNameAsync(addonCategoryId, categoryName, cancellationToken);

        var toppingRes = AcaiFreeTopping.Create(
            guard.Value,
            name,
            maxPortionsPerCup,
            displayOrder,
            supplyItemId,
            consumptionQuantityPerPortion,
            consumptionUnitCode,
            description,
            costRes.Value,
            addonCategoryId,
            resolvedCategoryName);

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
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal costPriceAmount = 0m,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<AcaiPaidAddon>(guard.Error);

        var priceRes = Money.Create(additionalPriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<AcaiPaidAddon>(priceRes.Error);

        var costRes = Money.Create(Math.Max(0m, costPriceAmount));
        if (costRes.IsFailure)
            return Result.Failure<AcaiPaidAddon>(costRes.Error);

        var resolvedCategoryName = await ResolveAddonCategoryNameAsync(addonCategoryId, categoryName, cancellationToken);

        var addonRes = AcaiPaidAddon.Create(
            guard.Value,
            name,
            priceRes.Value,
            maxQuantityPerCup,
            displayOrder,
            supplyItemId,
            consumptionQuantityPerUnit,
            consumptionUnitCode,
            description,
            costRes.Value,
            addonCategoryId,
            resolvedCategoryName);

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

    public async Task<Result<AcaiCupSize>> UpdateCupSizeAsync(
        User actor,
        Guid cupSizeId,
        string name,
        int volumeMl,
        decimal basePriceAmount,
        int freeToppingsLimit,
        int minFreeToppings = 0,
        AcaiFreeCountingMode countingMode = AcaiFreeCountingMode.ByPortion,
        int? maxPaidAddonsPerCup = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal? costPriceAmount = null,
        bool? allowFreeToppings = null,
        bool? allowPaidAddons = null)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<AcaiCupSize>(guard.Error);

        var cup = await _cupRepo.FindByIdAsync(cupSizeId, cancellationToken);
        if (cup is null)
            return Result.Failure<AcaiCupSize>(Error.NotFound("Acai.CupSizeNotFound", "Tamanho de copo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, cup);
        var priceRes = Money.Create(basePriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<AcaiCupSize>(priceRes.Error);

        Money? costMoney = null;
        if (costPriceAmount.HasValue)
        {
            var cRes = Money.Create(Math.Max(0m, costPriceAmount.Value));
            if (cRes.IsFailure) return Result.Failure<AcaiCupSize>(cRes.Error);
            costMoney = cRes.Value;
        }

        var res = cup.UpdateConfiguration(
            name,
            volumeMl,
            priceRes.Value,
            freeToppingsLimit,
            minFreeToppings,
            countingMode,
            maxPaidAddonsPerCup,
            displayOrder,
            description,
            costMoney,
            allowFreeToppings,
            allowPaidAddons);
        if (res.IsFailure)
            return Result.Failure<AcaiCupSize>(res.Error);

        await _cupRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cup);
    }

    public async Task<Result> ReorderCupSizesAsync(User actor, IReadOnlyDictionary<Guid, int> newOrders, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var cups = await _cupRepo.ListAsync(true, cancellationToken);
        foreach (var c in cups)
        {
            var tracked = await _cupRepo.FindByIdAsync(c.Id, cancellationToken);
            if (tracked is null) continue;
            TenantGuard.EnsureOwnership(_tenantContext, tracked);
            if (newOrders.TryGetValue(tracked.Id, out var ord))
                tracked.ChangeOrder(ord);
        }

        await _cupRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetCupSizeActiveAsync(User actor, Guid cupSizeId, bool isActive, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var cup = await _cupRepo.FindByIdAsync(cupSizeId, cancellationToken);
        if (cup is null)
            return Result.Failure(Error.NotFound("Acai.CupSizeNotFound", "Tamanho de copo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, cup);
        if (isActive) cup.Reactivate(); else cup.Pause();
        await _cupRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteCupSizeAsync(User actor, Guid cupSizeId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure) return guard;

        var cup = await _cupRepo.FindByIdAsync(cupSizeId, cancellationToken);
        if (cup is null)
            return Result.Failure(Error.NotFound("Acai.CupSizeNotFound", "Tamanho de copo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, cup);
        _cupRepo.Remove(cup);
        await _cupRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AcaiBaseFlavor>> UpdateBaseFlavorAsync(User actor, Guid flavorId, string name, decimal extraPriceAmount = 0m, int displayOrder = 0, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return Result.Failure<AcaiBaseFlavor>(guard.Error);

        var flavor = await _flavorRepo.FindByIdAsync(flavorId, cancellationToken);
        if (flavor is null)
            return Result.Failure<AcaiBaseFlavor>(Error.NotFound("Acai.FlavorNotFound", "Sabor base não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, flavor);
        var priceRes = Money.Create(extraPriceAmount);
        if (priceRes.IsFailure) return Result.Failure<AcaiBaseFlavor>(priceRes.Error);

        var res = flavor.Update(name, priceRes.Value, displayOrder);
        if (res.IsFailure) return Result.Failure<AcaiBaseFlavor>(res.Error);

        await _flavorRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(flavor);
    }

    public async Task<Result> DeleteBaseFlavorAsync(User actor, Guid flavorId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure) return guard;

        var flavor = await _flavorRepo.FindByIdAsync(flavorId, cancellationToken);
        if (flavor is null)
            return Result.Failure(Error.NotFound("Acai.FlavorNotFound", "Sabor base não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, flavor);
        _flavorRepo.Remove(flavor);
        await _flavorRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AcaiFreeTopping>> UpdateFreeToppingAsync(
        User actor,
        Guid toppingId,
        string name,
        int maxPortionsPerCup = 1,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerPortion = null,
        string? consumptionUnitCode = null,
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal? costPriceAmount = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return Result.Failure<AcaiFreeTopping>(guard.Error);

        var topping = await _toppingRepo.FindByIdAsync(toppingId, cancellationToken);
        if (topping is null)
            return Result.Failure<AcaiFreeTopping>(Error.NotFound("Acai.ToppingNotFound", "Acompanhamento gratuito não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, topping);
        Money? costMoney = null;
        if (costPriceAmount.HasValue)
        {
            var cRes = Money.Create(Math.Max(0m, costPriceAmount.Value));
            if (cRes.IsFailure) return Result.Failure<AcaiFreeTopping>(cRes.Error);
            costMoney = cRes.Value;
        }
        var resolvedCategoryName = await ResolveAddonCategoryNameAsync(addonCategoryId, categoryName, cancellationToken);

        var res = topping.Update(name, maxPortionsPerCup, displayOrder, supplyItemId, consumptionQuantityPerPortion, consumptionUnitCode, description, costMoney, addonCategoryId, resolvedCategoryName);
        if (res.IsFailure) return Result.Failure<AcaiFreeTopping>(res.Error);

        await _toppingRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(topping);
    }

    public async Task<Result> ReactivateFreeToppingAsync(User actor, Guid toppingId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var topping = await _toppingRepo.FindByIdAsync(toppingId, cancellationToken);
        if (topping is null)
            return Result.Failure(Error.NotFound("Acai.ToppingNotFound", "Acompanhamento gratuito não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, topping);
        topping.Reactivate();
        await _toppingRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteFreeToppingAsync(User actor, Guid toppingId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure) return guard;

        var topping = await _toppingRepo.FindByIdAsync(toppingId, cancellationToken);
        if (topping is null)
            return Result.Failure(Error.NotFound("Acai.ToppingNotFound", "Acompanhamento gratuito não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, topping);
        _toppingRepo.Remove(topping);
        await _toppingRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AcaiPaidAddon>> UpdatePaidAddonAsync(
        User actor,
        Guid addonId,
        string name,
        decimal additionalPriceAmount,
        int maxQuantityPerCup = 3,
        int displayOrder = 0,
        Guid? supplyItemId = null,
        decimal? consumptionQuantityPerUnit = null,
        string? consumptionUnitCode = null,
        CancellationToken cancellationToken = default,
        string? description = null,
        decimal? costPriceAmount = null,
        Guid? addonCategoryId = null,
        string? categoryName = null)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return Result.Failure<AcaiPaidAddon>(guard.Error);

        var addon = await _addonRepo.FindByIdAsync(addonId, cancellationToken);
        if (addon is null)
            return Result.Failure<AcaiPaidAddon>(Error.NotFound("Acai.AddonNotFound", "Adicional pago não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, addon);
        var priceRes = Money.Create(additionalPriceAmount);
        if (priceRes.IsFailure) return Result.Failure<AcaiPaidAddon>(priceRes.Error);

        Money? costMoney = null;
        if (costPriceAmount.HasValue)
        {
            var cRes = Money.Create(Math.Max(0m, costPriceAmount.Value));
            if (cRes.IsFailure) return Result.Failure<AcaiPaidAddon>(cRes.Error);
            costMoney = cRes.Value;
        }
        var resolvedCategoryName = await ResolveAddonCategoryNameAsync(addonCategoryId, categoryName, cancellationToken);

        var res = addon.Update(name, priceRes.Value, maxQuantityPerCup, displayOrder, supplyItemId, consumptionQuantityPerUnit, consumptionUnitCode, description, costMoney, addonCategoryId, resolvedCategoryName);
        if (res.IsFailure) return Result.Failure<AcaiPaidAddon>(res.Error);

        await _addonRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(addon);
    }

    public async Task<Result<AcaiAddonCategory>> CreateAddonCategoryAsync(
        User actor,
        string name,
        AcaiAddonCategoryKind kind = AcaiAddonCategoryKind.FreeIncluded,
        bool isFreeByDefault = true,
        string? description = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        if (_addonCategoryRepo is null)
            return Result.Failure<AcaiAddonCategory>(Error.Conflict("Acai.CategoryRepoUnavailable", "Repositório de categorias de açaí indisponível."));
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure) return Result.Failure<AcaiAddonCategory>(guard.Error);

        var res = AcaiAddonCategory.Create(guard.Value, name, kind, isFreeByDefault, description, displayOrder);
        if (res.IsFailure) return res;

        await _addonCategoryRepo.AddAsync(res.Value, cancellationToken);
        await _addonCategoryRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<AcaiAddonCategory>> UpdateAddonCategoryAsync(
        User actor,
        Guid categoryId,
        string name,
        AcaiAddonCategoryKind kind,
        bool isFreeByDefault,
        string? description = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        if (_addonCategoryRepo is null)
            return Result.Failure<AcaiAddonCategory>(Error.Conflict("Acai.CategoryRepoUnavailable", "Repositório de categorias de açaí indisponível."));
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return Result.Failure<AcaiAddonCategory>(guard.Error);

        var cat = await _addonCategoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (cat is null) return Result.Failure<AcaiAddonCategory>(Error.NotFound("Acai.AddonCategoryNotFound", "Categoria de adicional não encontrada."));
        TenantGuard.EnsureOwnership(_tenantContext, cat);

        var upd = cat.Update(name, kind, isFreeByDefault, description, displayOrder);
        if (upd.IsFailure) return Result.Failure<AcaiAddonCategory>(upd.Error);

        await _addonCategoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cat);
    }

    public async Task<Result> DeleteAddonCategoryAsync(User actor, Guid categoryId, CancellationToken cancellationToken = default)
    {
        if (_addonCategoryRepo is null)
            return Result.Failure(Error.Conflict("Acai.CategoryRepoUnavailable", "Repositório de categorias de açaí indisponível."));
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure) return guard;

        var cat = await _addonCategoryRepo.FindByIdAsync(categoryId, cancellationToken);
        if (cat is null) return Result.Failure(Error.NotFound("Acai.AddonCategoryNotFound", "Categoria de adicional não encontrada."));
        TenantGuard.EnsureOwnership(_tenantContext, cat);

        _addonCategoryRepo.Remove(cat);
        await _addonCategoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderAddonCategoriesAsync(User actor, IReadOnlyDictionary<Guid, int> newOrders, CancellationToken cancellationToken = default)
    {
        if (_addonCategoryRepo is null) return Result.Success();
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var cats = await _addonCategoryRepo.ListAsync(true, cancellationToken);
        foreach (var c in cats)
        {
            var tracked = await _addonCategoryRepo.FindByIdAsync(c.Id, cancellationToken);
            if (tracked is null) continue;
            TenantGuard.EnsureOwnership(_tenantContext, tracked);
            if (newOrders.TryGetValue(tracked.Id, out var ord))
                tracked.ChangeOrder(ord);
        }
        await _addonCategoryRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> MoveAcaiAddonAsync(
        User actor,
        Guid itemId,
        bool sourceIsFree,
        bool targetIsFree,
        Guid? targetAddonCategoryId = null,
        string? targetCategoryName = null,
        decimal? salePriceIfPaid = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var resolvedCategoryName = await ResolveAddonCategoryNameAsync(targetAddonCategoryId, targetCategoryName, cancellationToken);

        if (sourceIsFree)
        {
            var topping = await _toppingRepo.FindByIdAsync(itemId, cancellationToken);
            if (topping is null)
                return Result.Failure(Error.NotFound("Acai.ToppingNotFound", "Acompanhamento gratuito não encontrado."));
            TenantGuard.EnsureOwnership(_tenantContext, topping);

            if (targetIsFree)
            {
                var upd = topping.Update(topping.Name, topping.MaxPortionsPerCup, topping.DisplayOrder, topping.SupplyItemId, topping.ConsumptionQuantityPerPortion, topping.ConsumptionUnitCode, topping.Description, topping.CostPrice, targetAddonCategoryId ?? Guid.Empty, resolvedCategoryName ?? string.Empty);
                if (upd.IsFailure) return upd;
                await _toppingRepo.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            var priceRes = Money.Create(Math.Max(0m, salePriceIfPaid ?? 3.50m));
            if (priceRes.IsFailure) return priceRes;

            var paidRes = AcaiPaidAddon.Create(
                guard.Value,
                topping.Name,
                priceRes.Value,
                Math.Max(1, topping.MaxPortionsPerCup),
                topping.DisplayOrder,
                topping.SupplyItemId,
                topping.ConsumptionQuantityPerPortion,
                topping.ConsumptionUnitCode,
                topping.Description,
                topping.CostPrice,
                targetAddonCategoryId,
                resolvedCategoryName);
            if (paidRes.IsFailure) return paidRes;

            if (!topping.IsActive) paidRes.Value.Pause();
            await _addonRepo.AddAsync(paidRes.Value, cancellationToken);
            _toppingRepo.Remove(topping);
            await _addonRepo.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        else
        {
            var addon = await _addonRepo.FindByIdAsync(itemId, cancellationToken);
            if (addon is null)
                return Result.Failure(Error.NotFound("Acai.AddonNotFound", "Adicional pago não encontrado."));
            TenantGuard.EnsureOwnership(_tenantContext, addon);

            if (!targetIsFree)
            {
                var upd = addon.Update(addon.Name, addon.AdditionalPrice, addon.MaxQuantityPerCup, addon.DisplayOrder, addon.SupplyItemId, addon.ConsumptionQuantityPerUnit, addon.ConsumptionUnitCode, addon.Description, addon.CostPrice, targetAddonCategoryId ?? Guid.Empty, resolvedCategoryName ?? string.Empty);
                if (upd.IsFailure) return upd;
                await _addonRepo.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            var freeRes = AcaiFreeTopping.Create(
                guard.Value,
                addon.Name,
                Math.Max(1, addon.MaxQuantityPerCup),
                addon.DisplayOrder,
                addon.SupplyItemId,
                addon.ConsumptionQuantityPerUnit,
                addon.ConsumptionUnitCode,
                addon.Description,
                addon.CostPrice,
                targetAddonCategoryId,
                resolvedCategoryName);
            if (freeRes.IsFailure) return freeRes;

            if (!addon.IsActive) freeRes.Value.Pause();
            await _toppingRepo.AddAsync(freeRes.Value, cancellationToken);
            _addonRepo.Remove(addon);
            await _toppingRepo.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }

    private async Task<string?> ResolveAddonCategoryNameAsync(Guid? categoryId, string? explicitName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName.Trim();
        if (categoryId.HasValue && categoryId.Value != Guid.Empty && _addonCategoryRepo is not null)
        {
            var cat = await _addonCategoryRepo.FindByIdAsync(categoryId.Value, ct);
            if (cat is not null) return cat.Name;
        }
        return null;
    }

    public async Task<Result> ReactivatePaidAddonAsync(User actor, Guid addonId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure) return guard;

        var addon = await _addonRepo.FindByIdAsync(addonId, cancellationToken);
        if (addon is null)
            return Result.Failure(Error.NotFound("Acai.AddonNotFound", "Adicional pago não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, addon);
        addon.Reactivate();
        await _addonRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeletePaidAddonAsync(User actor, Guid addonId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure) return guard;

        var addon = await _addonRepo.FindByIdAsync(addonId, cancellationToken);
        if (addon is null)
            return Result.Failure(Error.NotFound("Acai.AddonNotFound", "Adicional pago não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, addon);
        _addonRepo.Remove(addon);
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
        var addonCats = _addonCategoryRepo is not null
            ? await _addonCategoryRepo.ListAsync(includePaused, cancellationToken)
            : Array.Empty<AcaiAddonCategory>();

        return new AcaiMenuCatalogView(cups, flavors, free, paid, addonCats);
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
