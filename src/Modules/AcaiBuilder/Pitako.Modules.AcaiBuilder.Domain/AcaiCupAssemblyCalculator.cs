using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.AcaiBuilder.Domain;

public sealed record AcaiItemSelection(Guid ItemId, int Quantity = 1);

public sealed record AcaiCupBuildRequest(
    Guid CupSizeId,
    IReadOnlyList<Guid> BaseFlavorIds,
    IReadOnlyList<AcaiItemSelection> FreeToppings,
    IReadOnlyList<AcaiItemSelection> PaidAddons,
    int CupQuantity = 1,
    string? CustomerNotes = null);

public sealed record AcaiSelectedFlavorSummary(Guid FlavorId, string Name, Money ExtraPrice);

public sealed record AcaiSelectedFreeToppingSummary(Guid ToppingId, string Name, int Quantity);

public sealed record AcaiSelectedPaidAddonSummary(Guid AddonId, string Name, int Quantity, Money UnitPrice, Money Subtotal);

/// <summary>
/// Resumo estruturado e validado no backend para exibição no cardápio digital,
/// preservação por item no carrinho, PDV e impressão em produção (Seções 13 e 17).
/// </summary>
public sealed record AcaiCupAssemblySummary(
    Guid CupSizeId,
    string CupSizeName,
    int VolumeMl,
    Money CupBasePrice,
    IReadOnlyList<AcaiSelectedFlavorSummary> BaseFlavors,
    Money BaseFlavorsExtraTotal,
    int FreeToppingsLimit,
    int FreeToppingsUsedCount,
    int FreeToppingsRemainingCount,
    IReadOnlyList<AcaiSelectedFreeToppingSummary> FreeToppings,
    IReadOnlyList<AcaiSelectedPaidAddonSummary> PaidAddons,
    Money PaidAddonsTotal,
    Money UnitFinalPrice,
    int CupQuantity,
    Money TotalPrice,
    string? CustomerNotes);

/// <summary>
/// Validador e calculadora autoritativa de copos personalizados de açaí.
/// Impede fraude de limites gratuitos ou preços enviados pelo navegador (Seção 13).
/// </summary>
public static class AcaiCupAssemblyCalculator
{
    public static Result<AcaiCupAssemblySummary> ValidateAndBuild(
        Guid tenantId,
        AcaiCupSize cupSize,
        IReadOnlyDictionary<Guid, AcaiBaseFlavor> flavorLookup,
        IReadOnlyDictionary<Guid, AcaiFreeTopping> freeToppingLookup,
        IReadOnlyDictionary<Guid, AcaiPaidAddon> paidAddonLookup,
        AcaiCupBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(cupSize);
        ArgumentNullException.ThrowIfNull(request);

        if (cupSize.TenantId != tenantId || !cupSize.IsActive)
            return Result.Failure<AcaiCupAssemblySummary>(Error.NotFound("Acai.CupSizeUnavailable", "Tamanho de copo indisponível ou pertencente a outro estabelecimento."));

        if (request.CupQuantity <= 0)
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.InvalidCupQuantity", "Quantidade de copos deve ser positiva."));

        var currency = cupSize.BasePrice.Currency;
        var selectedFlavors = new List<AcaiSelectedFlavorSummary>();
        var flavorExtraTotal = Money.Zero(currency);

        foreach (var flavorId in (request.BaseFlavorIds ?? []).Distinct())
        {
            if (!flavorLookup.TryGetValue(flavorId, out var flavor) || flavor.TenantId != tenantId || !flavor.IsActive)
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.BaseFlavorUnavailable", "Sabor/creme base inválido ou pausado."));

            selectedFlavors.Add(new AcaiSelectedFlavorSummary(flavor.Id, flavor.Name, flavor.ExtraPrice));
            flavorExtraTotal = flavorExtraTotal.Add(flavor.ExtraPrice);
        }

        // Agrega por ItemId para impedir burla ao dividir seleções do mesmo item em múltiplas entradas.
        var groupedFree = (request.FreeToppings ?? [])
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, TotalQty = g.Sum(i => i.Quantity) })
            .ToList();

        var selectedFree = new List<AcaiSelectedFreeToppingSummary>();
        var usedFreeCount = 0;

        if (!cupSize.AllowFreeToppings && groupedFree.Count > 0)
        {
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                "Acai.FreeToppingsNotAllowed",
                $"O tamanho '{cupSize.Name}' está configurado para não incluir acompanhamentos gratuitos."));
        }

        foreach (var entry in groupedFree)
        {
            if (entry.TotalQty <= 0)
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.InvalidToppingQuantity", "Quantidade de acompanhamento deve ser positiva."));

            if (!freeToppingLookup.TryGetValue(entry.ItemId, out var topping) || topping.TenantId != tenantId || !topping.IsActive)
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.ToppingUnavailable", "Acompanhamento gratuito indisponível ou pausado."));

            if (entry.TotalQty > topping.MaxPortionsPerCup)
            {
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                    "Acai.ToppingMaxPortionsExceeded",
                    $"O acompanhamento '{topping.Name}' permite no máximo {topping.MaxPortionsPerCup} porção(ões) por copo."));
            }

            usedFreeCount += cupSize.CountingMode == AcaiFreeCountingMode.ByDistinctItem ? 1 : entry.TotalQty;
            selectedFree.Add(new AcaiSelectedFreeToppingSummary(topping.Id, topping.Name, entry.TotalQty));
        }

        if (cupSize.AllowFreeToppings && usedFreeCount > cupSize.FreeToppingsLimit)
        {
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                "Acai.FreeToppingsLimitExceeded",
                $"Limite de {cupSize.FreeToppingsLimit} acompanhamento(s) gratuito(s) excedido (selecionados: {usedFreeCount})."));
        }

        if (cupSize.AllowFreeToppings && usedFreeCount < cupSize.MinFreeToppings)
        {
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                "Acai.MinFreeToppingsRequired",
                $"Selecione ao menos {cupSize.MinFreeToppings} acompanhamento(s) gratuito(s)."));
        }

        var groupedPaid = (request.PaidAddons ?? [])
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, TotalQty = g.Sum(i => i.Quantity) })
            .ToList();

        if (!cupSize.AllowPaidAddons && groupedPaid.Count > 0)
        {
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                "Acai.PaidAddonsNotAllowed",
                $"O tamanho '{cupSize.Name}' não aceita adicionais pagos."));
        }

        var selectedPaid = new List<AcaiSelectedPaidAddonSummary>();
        var totalPaidQty = 0;
        var paidAddonsTotal = Money.Zero(currency);

        foreach (var entry in groupedPaid)
        {
            if (entry.TotalQty <= 0)
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.InvalidAddonQuantity", "Quantidade de adicional pago deve ser positiva."));

            if (!paidAddonLookup.TryGetValue(entry.ItemId, out var addon) || addon.TenantId != tenantId || !addon.IsActive)
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation("Acai.PaidAddonUnavailable", "Adicional pago indisponível ou pausado."));

            if (entry.TotalQty > addon.MaxQuantityPerCup)
            {
                return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                    "Acai.PaidAddonMaxQuantityExceeded",
                    $"O adicional pago '{addon.Name}' permite no máximo {addon.MaxQuantityPerCup} unidade(s) por copo."));
            }

            totalPaidQty += entry.TotalQty;
            var subtotal = addon.AdditionalPrice.Multiply(entry.TotalQty);
            paidAddonsTotal = paidAddonsTotal.Add(subtotal);
            selectedPaid.Add(new AcaiSelectedPaidAddonSummary(addon.Id, addon.Name, entry.TotalQty, addon.AdditionalPrice, subtotal));
        }

        if (cupSize.MaxPaidAddonsPerCup.HasValue && totalPaidQty > cupSize.MaxPaidAddonsPerCup.Value)
        {
            return Result.Failure<AcaiCupAssemblySummary>(Error.Validation(
                "Acai.MaxPaidAddonsExceeded",
                $"Este copo permite no máximo {cupSize.MaxPaidAddonsPerCup.Value} adicional(is) pago(s)."));
        }

        var unitFinalPrice = cupSize.BasePrice.Add(flavorExtraTotal).Add(paidAddonsTotal);
        var totalPrice = unitFinalPrice.Multiply(request.CupQuantity);
        var remainingFree = Math.Max(0, cupSize.FreeToppingsLimit - usedFreeCount);

        return Result.Success(new AcaiCupAssemblySummary(
            cupSize.Id,
            cupSize.Name,
            cupSize.VolumeMl,
            cupSize.BasePrice,
            selectedFlavors,
            flavorExtraTotal,
            cupSize.FreeToppingsLimit,
            usedFreeCount,
            remainingFree,
            selectedFree,
            selectedPaid,
            paidAddonsTotal,
            unitFinalPrice,
            request.CupQuantity,
            totalPrice,
            string.IsNullOrWhiteSpace(request.CustomerNotes) ? null : request.CustomerNotes.Trim()));
    }
}
