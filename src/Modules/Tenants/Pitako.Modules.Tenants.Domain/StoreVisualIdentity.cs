using System.Text.RegularExpressions;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Domain;

/// <summary>
/// Configuração visual publicada ou em rascunho de um estabelecimento.
/// Totalmente independente da marca PITAKO (Seção 3).
/// </summary>
public sealed record BrandingPalette(
    string PrimaryColorHex,
    string SecondaryColorHex,
    string AccentColorHex)
{
    private static readonly Regex HexRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Paleta neutra inicial (não impõe vermelho nem a marca PITAKO às lojas clientes).
    /// </summary>
    public static BrandingPalette NeutralDefault() => new("#1E293B", "#F8FAFC", "#0F766E");

    public static Result<BrandingPalette> Create(string primaryHex, string secondaryHex, string accentHex)
    {
        if (!IsValidHex(primaryHex) || !IsValidHex(secondaryHex) || !IsValidHex(accentHex))
            return Result.Failure<BrandingPalette>(Error.Validation("Branding.InvalidColor", "As cores devem estar no formato hexadecimal #RRGGBB."));

        return Result.Success(new BrandingPalette(
            primaryHex.Trim().ToUpperInvariant(),
            secondaryHex.Trim().ToUpperInvariant(),
            accentHex.Trim().ToUpperInvariant()));
    }

    private static bool IsValidHex(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexRegex.IsMatch(value.Trim());
}

/// <summary>
/// Banner promocional configurável no cardápio digital da loja (rascunho ou publicado).
/// </summary>
public sealed class StoreBanner : Entity<Guid>
{
    private StoreBanner()
    {
        Title = string.Empty;
        ImageUrl = string.Empty;
    }

    internal StoreBanner(
        Guid id,
        Guid visualIdentityId,
        Guid tenantId,
        string title,
        string imageUrl,
        string? targetUrl,
        int displayOrder,
        bool isDraft) : base(id)
    {
        VisualIdentityId = visualIdentityId;
        TenantId = tenantId;
        Title = title;
        ImageUrl = imageUrl;
        TargetUrl = targetUrl;
        DisplayOrder = displayOrder;
        IsDraft = isDraft;
    }

    public Guid VisualIdentityId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string ImageUrl { get; private set; }
    public string? TargetUrl { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsDraft { get; private set; }
}

public sealed record StoreBrandingSnapshot(
    string? LogoUrl,
    string? CoverImageUrl,
    string? Slogan,
    string PrimaryColorHex,
    string SecondaryColorHex,
    string AccentColorHex,
    string MenuLayoutMode,
    IReadOnlyList<StoreBannerSnapshot> Banners);

public sealed record StoreBannerSnapshot(
    string Title,
    string ImageUrl,
    string? TargetUrl,
    int DisplayOrder);

/// <summary>
/// Agregado isolado por tenant para personalização completa da identidade visual do estabelecimento,
/// com fluxo rascunho -> pré-visualização -> publicação (Seção 3).
/// </summary>
public sealed class StoreVisualIdentity : TenantScopedAggregateRoot
{
    private readonly List<StoreBanner> _banners = new();

    private StoreVisualIdentity()
    {
        DraftPrimaryColorHex = string.Empty;
        DraftSecondaryColorHex = string.Empty;
        DraftAccentColorHex = string.Empty;
        DraftMenuLayoutMode = "grid";
        PublishedPrimaryColorHex = string.Empty;
        PublishedSecondaryColorHex = string.Empty;
        PublishedAccentColorHex = string.Empty;
        PublishedMenuLayoutMode = "grid";
    }

    private StoreVisualIdentity(Guid id, Guid tenantId, BrandingPalette palette)
        : base(id, tenantId)
    {
        DraftPrimaryColorHex = palette.PrimaryColorHex;
        DraftSecondaryColorHex = palette.SecondaryColorHex;
        DraftAccentColorHex = palette.AccentColorHex;
        DraftMenuLayoutMode = "grid";

        PublishedPrimaryColorHex = palette.PrimaryColorHex;
        PublishedSecondaryColorHex = palette.SecondaryColorHex;
        PublishedAccentColorHex = palette.AccentColorHex;
        PublishedMenuLayoutMode = "grid";
        PublishedVersion = 0;
    }

    // Estado Rascunho (editável e pré-visualizável antes de publicar)
    public string? DraftLogoUrl { get; private set; }
    public string? DraftCoverImageUrl { get; private set; }
    public string? DraftSlogan { get; private set; }
    public string DraftPrimaryColorHex { get; private set; }
    public string DraftSecondaryColorHex { get; private set; }
    public string DraftAccentColorHex { get; private set; }
    public string DraftMenuLayoutMode { get; private set; }

    // Estado Publicado (exibido ao consumidor final)
    public string? PublishedLogoUrl { get; private set; }
    public string? PublishedCoverImageUrl { get; private set; }
    public string? PublishedSlogan { get; private set; }
    public string PublishedPrimaryColorHex { get; private set; }
    public string PublishedSecondaryColorHex { get; private set; }
    public string PublishedAccentColorHex { get; private set; }
    public string PublishedMenuLayoutMode { get; private set; }
    public int PublishedVersion { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public bool HasUnpublishedChanges { get; private set; }

    public IReadOnlyCollection<StoreBanner> Banners => _banners.AsReadOnly();

    public static StoreVisualIdentity CreateDefault(Guid tenantId) =>
        new(Guid.NewGuid(), tenantId, BrandingPalette.NeutralDefault());

    public Result UpdateDraftBranding(
        string? logoUrl,
        string? coverImageUrl,
        string? slogan,
        string primaryColorHex,
        string secondaryColorHex,
        string accentColorHex,
        string menuLayoutMode,
        IEnumerable<StoreBannerSnapshot>? draftBanners = null)
    {
        var paletteResult = BrandingPalette.Create(primaryColorHex, secondaryColorHex, accentColorHex);
        if (paletteResult.IsFailure)
            return paletteResult;

        var normalizedLayout = string.IsNullOrWhiteSpace(menuLayoutMode) ? "grid" : menuLayoutMode.Trim().ToLowerInvariant();
        if (normalizedLayout is not ("grid" or "list" or "showcase"))
            return Result.Failure(Error.Validation("Branding.InvalidLayout", "Layout de cardápio inválido (permitido: grid, list, showcase)."));

        DraftLogoUrl = NormalizeOptionalUrl(logoUrl);
        DraftCoverImageUrl = NormalizeOptionalUrl(coverImageUrl);
        DraftSlogan = string.IsNullOrWhiteSpace(slogan) ? null : slogan.Trim();
        DraftPrimaryColorHex = paletteResult.Value.PrimaryColorHex;
        DraftSecondaryColorHex = paletteResult.Value.SecondaryColorHex;
        DraftAccentColorHex = paletteResult.Value.AccentColorHex;
        DraftMenuLayoutMode = normalizedLayout;

        if (draftBanners is not null)
        {
            _banners.RemoveAll(b => b.IsDraft);
            foreach (var banner in draftBanners.OrderBy(b => b.DisplayOrder))
            {
                if (string.IsNullOrWhiteSpace(banner.Title) || string.IsNullOrWhiteSpace(banner.ImageUrl))
                    return Result.Failure(Error.Validation("Branding.InvalidBanner", "Banner exige título e URL da imagem."));

                _banners.Add(new StoreBanner(
                    Guid.NewGuid(),
                    Id,
                    TenantId,
                    banner.Title.Trim(),
                    banner.ImageUrl.Trim(),
                    NormalizeOptionalUrl(banner.TargetUrl),
                    banner.DisplayOrder,
                    isDraft: true));
            }
        }

        HasUnpublishedChanges = true;
        return Result.Success();
    }

    public StoreBrandingSnapshot GetDraftPreview() =>
        new(
            DraftLogoUrl,
            DraftCoverImageUrl,
            DraftSlogan,
            DraftPrimaryColorHex,
            DraftSecondaryColorHex,
            DraftAccentColorHex,
            DraftMenuLayoutMode,
            _banners.Where(b => b.IsDraft)
                .OrderBy(b => b.DisplayOrder)
                .Select(b => new StoreBannerSnapshot(b.Title, b.ImageUrl, b.TargetUrl, b.DisplayOrder))
                .ToList());

    public StoreBrandingSnapshot GetPublishedView() =>
        new(
            PublishedLogoUrl,
            PublishedCoverImageUrl,
            PublishedSlogan,
            PublishedPrimaryColorHex,
            PublishedSecondaryColorHex,
            PublishedAccentColorHex,
            PublishedMenuLayoutMode,
            _banners.Where(b => !b.IsDraft)
                .OrderBy(b => b.DisplayOrder)
                .Select(b => new StoreBannerSnapshot(b.Title, b.ImageUrl, b.TargetUrl, b.DisplayOrder))
                .ToList());

    public void Publish(DateTimeOffset utcNow)
    {
        PublishedLogoUrl = DraftLogoUrl;
        PublishedCoverImageUrl = DraftCoverImageUrl;
        PublishedSlogan = DraftSlogan;
        PublishedPrimaryColorHex = DraftPrimaryColorHex;
        PublishedSecondaryColorHex = DraftSecondaryColorHex;
        PublishedAccentColorHex = DraftAccentColorHex;
        PublishedMenuLayoutMode = DraftMenuLayoutMode;

        _banners.RemoveAll(b => !b.IsDraft);
        var draftCopy = _banners.Where(b => b.IsDraft).ToList();
        foreach (var draft in draftCopy)
        {
            _banners.Add(new StoreBanner(
                Guid.NewGuid(),
                Id,
                TenantId,
                draft.Title,
                draft.ImageUrl,
                draft.TargetUrl,
                draft.DisplayOrder,
                isDraft: false));
        }

        PublishedVersion++;
        PublishedAtUtc = utcNow;
        HasUnpublishedChanges = false;
    }

    public void DiscardDraft()
    {
        DraftLogoUrl = PublishedLogoUrl;
        DraftCoverImageUrl = PublishedCoverImageUrl;
        DraftSlogan = PublishedSlogan;
        DraftPrimaryColorHex = PublishedPrimaryColorHex;
        DraftSecondaryColorHex = PublishedSecondaryColorHex;
        DraftAccentColorHex = PublishedAccentColorHex;
        DraftMenuLayoutMode = PublishedMenuLayoutMode;

        _banners.RemoveAll(b => b.IsDraft);
        var publishedCopy = _banners.Where(b => !b.IsDraft).ToList();
        foreach (var pub in publishedCopy)
        {
            _banners.Add(new StoreBanner(
                Guid.NewGuid(),
                Id,
                TenantId,
                pub.Title,
                pub.ImageUrl,
                pub.TargetUrl,
                pub.DisplayOrder,
                isDraft: true));
        }

        HasUnpublishedChanges = false;
    }

    private static string? NormalizeOptionalUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
