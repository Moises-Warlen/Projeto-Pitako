using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Application.Services;

public sealed record SupplyPriceUpdateResult(
    SupplyItem SupplyItem,
    int RecalculatedTechnicalSheetsCount);

/// <summary>
/// Caso de uso de insumos, conversões de unidade, fichas técnicas com recálculo automático
/// em cascata e simulação de formação de preço (Seção 15).
/// </summary>
public sealed class SuppliesAndTechnicalSheetService
{
    private readonly ITenantCustomUnitRepository _unitRepo;
    private readonly ISupplyItemRepository _supplyRepo;
    private readonly ITechnicalSheetRepository _sheetRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public SuppliesAndTechnicalSheetService(
        ITenantCustomUnitRepository unitRepo,
        ISupplyItemRepository supplyRepo,
        ITechnicalSheetRepository sheetRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _unitRepo = unitRepo;
        _supplyRepo = supplyRepo;
        _sheetRepo = sheetRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<TenantCustomUnit>> RegisterCustomUnitAsync(
        User actor,
        string code,
        string name,
        UnitDimension dimension,
        decimal factorToBaseUnit,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<TenantCustomUnit>(guard.Error);

        var unitRes = TenantCustomUnit.Create(guard.Value, code, name, dimension, factorToBaseUnit);
        if (unitRes.IsFailure)
            return unitRes;

        await _unitRepo.AddAsync(unitRes.Value, cancellationToken);
        await _unitRepo.SaveChangesAsync(cancellationToken);
        return unitRes;
    }

    public async Task<Result<SupplyItem>> CreateSupplyItemAsync(
        User actor,
        string name,
        decimal purchasePriceAmount,
        decimal purchaseQuantity,
        string purchaseUnitCode,
        string? supplierName = null,
        string? sku = null,
        decimal? contentQuantityPerPackage = null,
        string? contentUnitCode = null,
        decimal defaultLossPercent = 0m,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<SupplyItem>(guard.Error);

        var priceRes = Money.Create(purchasePriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<SupplyItem>(priceRes.Error);

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var supplyRes = SupplyItem.Create(
            guard.Value,
            name,
            priceRes.Value,
            purchaseQuantity,
            purchaseUnitCode,
            supplierName,
            sku,
            contentQuantityPerPackage,
            contentUnitCode,
            customUnits,
            _clock.UtcNow,
            actor.Id,
            defaultLossPercent);

        if (supplyRes.IsFailure)
            return supplyRes;

        await _supplyRepo.AddAsync(supplyRes.Value, cancellationToken);
        await _supplyRepo.SaveChangesAsync(cancellationToken);
        return supplyRes;
    }

    public async Task<Result<SupplyPriceUpdateResult>> UpdateSupplyItemAsync(
        User actor,
        Guid supplyItemId,
        string name,
        decimal purchasePriceAmount,
        decimal purchaseQuantity,
        string purchaseUnitCode,
        string? supplierName,
        string? sku,
        decimal? contentQuantityPerPackage,
        string? contentUnitCode,
        decimal defaultLossPercent,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(guard.Error);

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        if (supply is null)
            return Result.Failure<SupplyPriceUpdateResult>(Error.NotFound("Supply.NotFound", "Insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, supply);
        var priceRes = Money.Create(purchasePriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(priceRes.Error);

        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
        var utcNow = _clock.UtcNow;
        var upd = supply.UpdateDetails(
            name,
            priceRes.Value,
            purchaseQuantity,
            purchaseUnitCode,
            supplierName,
            sku,
            contentQuantityPerPackage,
            contentUnitCode,
            defaultLossPercent,
            customUnits,
            utcNow,
            actor.Id);
        if (upd.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(upd.Error);

        if (isActive) supply.Activate();
        else supply.Deactivate();

        var affectedSheets = await _sheetRepo.ListUsingSupplyItemAsync(supplyItemId, cancellationToken);
        if (affectedSheets.Count > 0)
        {
            var allSupplyIds = affectedSheets
                .SelectMany(s => s.Ingredients.Select(i => i.SupplyItemId)
                    .Concat(s.Packagings.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value)))
                .Distinct()
                .ToList();

            var lookup = (await _supplyRepo.FindLookupByIdsAsync(allSupplyIds, cancellationToken))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            lookup[supply.Id] = supply;

            foreach (var sheet in affectedSheets)
            {
                var recalc = sheet.RecalculateFromCurrentSupplies(lookup, customUnits, $"Atualização do insumo '{supply.Name}'", utcNow);
                if (recalc.IsFailure)
                    return Result.Failure<SupplyPriceUpdateResult>(recalc.Error);
            }
        }

        await _supplyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(new SupplyPriceUpdateResult(supply, affectedSheets.Count));
    }

    public async Task<Result> DeleteSupplyItemAsync(User actor, Guid supplyItemId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        if (supply is null)
            return Result.Failure(Error.NotFound("Supply.NotFound", "Insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, supply);
        var usedBySheets = await _sheetRepo.ListUsingSupplyItemAsync(supplyItemId, cancellationToken);
        if (usedBySheets.Count > 0)
            return Result.Failure(Error.Conflict("Supply.InUse", $"Insumo vinculado a {usedBySheets.Count} ficha(s) técnica(s). Desative-o ou remova das fichas primeiro."));

        _supplyRepo.Remove(supply);
        await _supplyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<TechnicalSheet>> CreateTechnicalSheetAsync(
        User actor,
        string name,
        decimal yieldQuantity,
        string yieldUnitCode,
        IEnumerable<IngredientInput> ingredients,
        IEnumerable<PackagingInput> packagings,
        Guid? productId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<TechnicalSheet>(guard.Error);

        var ingList = ingredients.ToList();
        var pkgList = packagings.ToList();
        var supplyIds = ingList.Select(i => i.SupplyItemId)
            .Concat(pkgList.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value))
            .Distinct()
            .ToList();

        var supplies = await _supplyRepo.FindLookupByIdsAsync(supplyIds, cancellationToken);
        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);

        var sheetRes = TechnicalSheet.Create(
            guard.Value,
            name,
            yieldQuantity,
            yieldUnitCode,
            ingList,
            pkgList,
            supplies,
            customUnits,
            _clock.UtcNow,
            productId,
            notes);

        if (sheetRes.IsFailure)
            return sheetRes;

        await _sheetRepo.AddAsync(sheetRes.Value, cancellationToken);
        await _sheetRepo.SaveChangesAsync(cancellationToken);
        return sheetRes;
    }

    public async Task<Result<TechnicalSheet>> UpdateTechnicalSheetAsync(
        User actor,
        Guid sheetId,
        string name,
        decimal yieldQuantity,
        string yieldUnitCode,
        IEnumerable<IngredientInput> ingredients,
        IEnumerable<PackagingInput> packagings,
        Guid? productId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<TechnicalSheet>(guard.Error);

        var sheet = await _sheetRepo.FindByIdAsync(sheetId, cancellationToken);
        if (sheet is null)
            return Result.Failure<TechnicalSheet>(Error.NotFound("TechnicalSheet.NotFound", "Ficha técnica não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, sheet);
        var metaRes = sheet.UpdateMetadata(name, yieldQuantity, yieldUnitCode, productId, notes);
        if (metaRes.IsFailure)
            return Result.Failure<TechnicalSheet>(metaRes.Error);

        var ingList = ingredients.ToList();
        var pkgList = packagings.ToList();
        var supplyIds = ingList.Select(i => i.SupplyItemId)
            .Concat(pkgList.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value))
            .Distinct()
            .ToList();

        var supplies = await _supplyRepo.FindLookupByIdsAsync(supplyIds, cancellationToken);
        var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);

        var compRes = sheet.ReplaceCompositionAndRecalculate(ingList, pkgList, supplies, customUnits, "Edição da ficha técnica", _clock.UtcNow);
        if (compRes.IsFailure)
            return Result.Failure<TechnicalSheet>(compRes.Error);

        await _sheetRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(sheet);
    }

    public async Task<Result> DeleteTechnicalSheetAsync(User actor, Guid sheetId, CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Delete);
        if (guard.IsFailure)
            return guard;

        var sheet = await _sheetRepo.FindByIdAsync(sheetId, cancellationToken);
        if (sheet is null)
            return Result.Failure(Error.NotFound("TechnicalSheet.NotFound", "Ficha técnica não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, sheet);
        _sheetRepo.Remove(sheet);
        await _sheetRepo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Atualiza preço do insumo, grava histórico e recalcula todas as fichas técnicas dependentes,
    /// preservando o histórico de custos de cada ficha.
    /// </summary>
    public async Task<Result<SupplyPriceUpdateResult>> UpdateSupplyPriceAsync(
        User actor,
        Guid supplyItemId,
        decimal newPriceAmount,
        string? supplierName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(guard.Error);

        var supply = await _supplyRepo.FindByIdAsync(supplyItemId, cancellationToken);
        if (supply is null)
            return Result.Failure<SupplyPriceUpdateResult>(Error.NotFound("Supply.NotFound", "Insumo não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, supply);

        var priceRes = Money.Create(newPriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(priceRes.Error);

        var utcNow = _clock.UtcNow;
        var updateRes = supply.UpdatePurchasePrice(priceRes.Value, supplierName, reason, utcNow, actor.Id);
        if (updateRes.IsFailure)
            return Result.Failure<SupplyPriceUpdateResult>(updateRes.Error);

        var affectedSheets = await _sheetRepo.ListUsingSupplyItemAsync(supplyItemId, cancellationToken);
        if (affectedSheets.Count > 0)
        {
            var customUnits = await _unitRepo.GetLookupAsync(cancellationToken);
            var allSupplyIds = affectedSheets
                .SelectMany(s => s.Ingredients.Select(i => i.SupplyItemId)
                    .Concat(s.Packagings.Where(p => p.SupplyItemId.HasValue).Select(p => p.SupplyItemId!.Value)))
                .Distinct()
                .ToList();

            var lookup = (await _supplyRepo.FindLookupByIdsAsync(allSupplyIds, cancellationToken))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            lookup[supply.Id] = supply;

            foreach (var sheet in affectedSheets)
            {
                var recalc = sheet.RecalculateFromCurrentSupplies(
                    lookup,
                    customUnits,
                    $"Reajuste de insumo '{supply.Name}': {reason}",
                    utcNow);

                if (recalc.IsFailure)
                    return Result.Failure<SupplyPriceUpdateResult>(recalc.Error);
            }
        }

        await _supplyRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(new SupplyPriceUpdateResult(supply, affectedSheets.Count));
    }

    public async Task<Result<PriceFormationResult>> SimulateSellingPriceAsync(
        User actor,
        Guid technicalSheetId,
        decimal taxPercent,
        decimal channelFeePercent,
        decimal operationalOverheadPercent,
        decimal desiredNetMarginPercent,
        decimal? explicitMarkupMultiplier = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<PriceFormationResult>(guard.Error);

        var sheet = await _sheetRepo.FindByIdAsync(technicalSheetId, cancellationToken);
        if (sheet is null)
            return Result.Failure<PriceFormationResult>(Error.NotFound("TechnicalSheet.NotFound", "Ficha técnica não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, sheet);

        return PriceFormationCalculator.Calculate(new PriceFormationInput(
            sheet.CostPerPortion,
            taxPercent,
            channelFeePercent,
            operationalOverheadPercent,
            desiredNetMarginPercent,
            explicitMarkupMultiplier));
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar insumos/fichas de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Inventory, action))
            return Result.Failure<Guid>(Error.Forbidden("Inventory.Forbidden", "Permissão insuficiente para operar insumos e fichas técnicas."));

        return Result.Success(currentTenantId);
    }
}
