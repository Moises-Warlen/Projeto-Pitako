using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.DiningHall.Application.Abstractions;

namespace Pitako.Modules.DiningHall.Application.Services;

internal static class SelfServiceFormattingHelper
{
    public static (string Title, string SummaryText) FormatCustomAcai(AcaiCupAssemblySummary cup)
    {
        var flavorsText = string.Join(", ", cup.BaseFlavors.Select(f => f.Name));
        var freeText = cup.FreeToppings.Count > 0
            ? $" | Grátis: {string.Join(", ", cup.FreeToppings.Select(t => $"{t.Quantity}x {t.Name}"))}"
            : string.Empty;
        var paidText = cup.PaidAddons.Count > 0
            ? $" | Adicionais: {string.Join(", ", cup.PaidAddons.Select(a => $"{a.Quantity}x {a.Name}"))}"
            : string.Empty;

        var title = $"Açaí {cup.CupSizeName} ({cup.VolumeMl}ml)";
        var summaryText = $"Bases: {flavorsText}{freeText}{paidText}".Trim();
        return (title, summaryText);
    }

    public static async Task<SelfServiceStoreBranding> ResolveStoreOnlyBrandingAsync(
        ISelfServiceBrandingProvider provider,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var custom = await provider.GetBrandingAsync(tenantId, cancellationToken);
        if (custom is not null)
            return custom with { UsesPitakoBranding = false };

        return new SelfServiceStoreBranding(
            tenantId,
            "Estabelecimento",
            null,
            "#1E293B",
            "#0F172A",
            "#F59E0B",
            UsesPitakoBranding: false);
    }
}
