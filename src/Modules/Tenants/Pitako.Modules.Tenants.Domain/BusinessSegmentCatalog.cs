using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Domain;

/// <summary>
/// Metadados e recursos recomendados por segmento comercial (Seção 11).
/// Extensível para novos segmentos sem alterar o núcleo do sistema.
/// </summary>
public sealed record BusinessSegmentProfile(
    string Code,
    string DisplayName,
    bool SupportsAcaiBuilder,
    bool SupportsWeightScale,
    bool SupportsTableService,
    bool SupportsFractionalItems);

public static class BusinessSegmentCatalog
{
    public const string Pizzaria = "pizzaria";
    public const string Acaiteria = "acaiteria";
    public const string Hamburgueria = "hamburgueria";
    public const string Padaria = "padaria";
    public const string Sorveteria = "sorveteria";
    public const string Restaurante = "restaurante";
    public const string Adega = "adega";
    public const string Lanchonete = "lanchonete";
    public const string Pastelaria = "pastelaria";
    public const string Doceria = "doceria";

    private static readonly Dictionary<string, BusinessSegmentProfile> BuiltInProfiles = new(StringComparer.Ordinal)
    {
        [Pizzaria] = new(Pizzaria, "Pizzaria", false, false, true, true),
        [Acaiteria] = new(Acaiteria, "Açaíteria", true, true, true, false),
        [Hamburgueria] = new(Hamburgueria, "Hamburgueria", false, false, true, false),
        [Padaria] = new(Padaria, "Padaria", false, true, true, false),
        [Sorveteria] = new(Sorveteria, "Sorveteria", true, true, true, false),
        [Restaurante] = new(Restaurante, "Restaurante", false, true, true, false),
        [Adega] = new(Adega, "Adega", false, false, true, false),
        [Lanchonete] = new(Lanchonete, "Lanchonete", false, false, true, false),
        [Pastelaria] = new(Pastelaria, "Pastelaria", false, false, true, false),
        [Doceria] = new(Doceria, "Doceria", false, true, true, false)
    };

    public static IReadOnlyCollection<BusinessSegmentProfile> AllBuiltIn => BuiltInProfiles.Values;

    public static string Normalize(string segmentCode) =>
        string.IsNullOrWhiteSpace(segmentCode) ? string.Empty : segmentCode.Trim().ToLowerInvariant();

    /// <summary>
    /// Retorna o perfil padrão do segmento ou um perfil genérico extensível para segmentos futuros.
    /// </summary>
    public static Result<BusinessSegmentProfile> Resolve(string segmentCode)
    {
        var normalized = Normalize(segmentCode);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            return Result.Failure<BusinessSegmentProfile>(Error.Validation("Segment.InvalidCode", "Código de segmento comercial inválido."));

        if (BuiltInProfiles.TryGetValue(normalized, out var profile))
            return Result.Success(profile);

        // Suporta outros segmentos futuros mantendo núcleo compartilhado (Seção 11).
        return Result.Success(new BusinessSegmentProfile(normalized, normalized, false, false, true, false));
    }
}
