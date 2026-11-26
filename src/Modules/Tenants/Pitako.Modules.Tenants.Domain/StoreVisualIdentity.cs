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

public sealed record StoreOperatingHoursEntry(
    int DayOfWeek,
    bool IsOpen,
    string OpenTime,
    string CloseTime);

public sealed record StoreOperatingStatus(
    bool IsOpenNow,
    string StatusLabel,
    string DetailText,
    bool IsTemporarilyPaused,
    string? PauseReason,
    IReadOnlyList<StoreOperatingHoursEntry> Schedule);

public sealed record StoreBrandingSnapshot(
    string? LogoUrl,
    string? CoverImageUrl,
    string? Slogan,
    string PrimaryColorHex,
    string SecondaryColorHex,
    string AccentColorHex,
    string MenuLayoutMode,
    IReadOnlyList<StoreBannerSnapshot> Banners,
    string? OperatingHoursJson = null,
    string? AnnouncementText = null,
    string? WhatsAppNumber = null,
    string? InstagramHandle = null,
    bool IsTemporarilyPaused = false,
    string? PauseReason = null);

public sealed record StoreBannerSnapshot(
    string Title,
    string ImageUrl,
    string? TargetUrl,
    int DisplayOrder);

public static class StoreOperatingHoursHelper
{
    public static readonly IReadOnlyList<StoreOperatingHoursEntry> DefaultSchedule =
    [
        new(0, true, "13:00", "23:00"),
        new(1, true, "11:00", "23:00"),
        new(2, true, "11:00", "23:00"),
        new(3, true, "11:00", "23:00"),
        new(4, true, "11:00", "23:00"),
        new(5, true, "11:00", "23:30"),
        new(6, true, "11:00", "23:30")
    ];

    public static IReadOnlyList<StoreOperatingHoursEntry> ParseSchedule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DefaultSchedule;
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<StoreOperatingHoursEntry>>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed is { Count: > 0 } ? parsed.OrderBy(p => p.DayOfWeek).ToList() : DefaultSchedule;
        }
        catch
        {
            return DefaultSchedule;
        }
    }

    public static StoreOperatingStatus Evaluate(
        string? scheduleJson,
        bool isTemporarilyPaused,
        string? pauseReason,
        DateTimeOffset utcNow,
        string? timeZoneId = "America/Sao_Paulo")
    {
        var schedule = ParseSchedule(scheduleJson);
        if (isTemporarilyPaused)
        {
            var reasonText = string.IsNullOrWhiteSpace(pauseReason) ? "Loja fechada temporariamente" : pauseReason.Trim();
            return new StoreOperatingStatus(false, "LOJA PAUSADA", reasonText, true, pauseReason, schedule);
        }

        DateTimeOffset localNow;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? "E. South America Standard Time" : timeZoneId);
            localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        }
        catch
        {
            localNow = utcNow.ToOffset(TimeSpan.FromHours(-3));
        }

        var dow = (int)localNow.DayOfWeek;
        var today = schedule.FirstOrDefault(s => s.DayOfWeek == dow);
        if (today is null || !today.IsOpen)
            return new StoreOperatingStatus(false, "LOJA FECHADA", "Fechado hoje conforme horário de funcionamento.", false, null, schedule);

        if (TimeSpan.TryParse(today.OpenTime, out var openTs) && TimeSpan.TryParse(today.CloseTime, out var closeTs))
        {
            var nowTod = localNow.TimeOfDay;
            var isWithin = closeTs >= openTs
                ? (nowTod >= openTs && nowTod <= closeTs)
                : (nowTod >= openTs || nowTod <= closeTs);
            return isWithin
                ? new StoreOperatingStatus(true, "ABERTA AGORA", $"Atendimento hoje até {today.CloseTime}.", false, null, schedule)
                : new StoreOperatingStatus(false, "LOJA FECHADA", $"Abre às {today.OpenTime} (horário de {today.OpenTime} às {today.CloseTime}).", false, null, schedule);
        }

        return new StoreOperatingStatus(true, "ABERTA AGORA", "Horário flexível ativo.", false, null, schedule);
    }
}

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
    public string? DraftOperatingHoursJson { get; private set; }
    public string? DraftAnnouncementText { get; private set; }
    public string? DraftWhatsAppNumber { get; private set; }
    public string? DraftInstagramHandle { get; private set; }

    // Estado Publicado (exibido ao consumidor final)
    public string? PublishedLogoUrl { get; private set; }
    public string? PublishedCoverImageUrl { get; private set; }
    public string? PublishedSlogan { get; private set; }
    public string PublishedPrimaryColorHex { get; private set; }
    public string PublishedSecondaryColorHex { get; private set; }
    public string PublishedAccentColorHex { get; private set; }
    public string PublishedMenuLayoutMode { get; private set; }
    public string? PublishedOperatingHoursJson { get; private set; }
    public string? PublishedAnnouncementText { get; private set; }
    public string? PublishedWhatsAppNumber { get; private set; }
    public string? PublishedInstagramHandle { get; private set; }
    public bool IsTemporarilyPaused { get; private set; }
    public string? PauseReason { get; private set; }
    public int PublishedVersion { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public bool HasUnpublishedChanges { get; private set; }

    public IReadOnlyCollection<StoreBanner> Banners => _banners.AsReadOnly();

    public static StoreVisualIdentity CreateDefault(Guid tenantId) =>
        new(Guid.NewGuid(), tenantId, BrandingPalette.NeutralDefault());

    public void SetTemporaryPause(bool isPaused, string? reason)
    {
        IsTemporarilyPaused = isPaused;
        PauseReason = isPaused && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;
    }

    public Result UpdateDraftBranding(
        string? logoUrl,
        string? coverImageUrl,
        string? slogan,
        string primaryColorHex,
        string secondaryColorHex,
        string accentColorHex,
        string menuLayoutMode,
        IEnumerable<StoreBannerSnapshot>? draftBanners = null,
        string? operatingHoursJson = null,
        string? announcementText = null,
        string? whatsAppNumber = null,
        string? instagramHandle = null)
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
        if (operatingHoursJson is not null)
            DraftOperatingHoursJson = string.IsNullOrWhiteSpace(operatingHoursJson) ? null : operatingHoursJson.Trim();
        if (announcementText is not null)
            DraftAnnouncementText = string.IsNullOrWhiteSpace(announcementText) ? null : announcementText.Trim();
        if (whatsAppNumber is not null)
            DraftWhatsAppNumber = string.IsNullOrWhiteSpace(whatsAppNumber) ? null : whatsAppNumber.Trim();
        if (instagramHandle is not null)
            DraftInstagramHandle = string.IsNullOrWhiteSpace(instagramHandle) ? null : instagramHandle.Trim();

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
                .ToList(),
            DraftOperatingHoursJson,
            DraftAnnouncementText,
            DraftWhatsAppNumber,
            DraftInstagramHandle,
            IsTemporarilyPaused,
            PauseReason);

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
                .ToList(),
            PublishedOperatingHoursJson,
            PublishedAnnouncementText,
            PublishedWhatsAppNumber,
            PublishedInstagramHandle,
            IsTemporarilyPaused,
            PauseReason);

    public void Publish(DateTimeOffset utcNow)
    {
        PublishedLogoUrl = DraftLogoUrl;
        PublishedCoverImageUrl = DraftCoverImageUrl;
        PublishedSlogan = DraftSlogan;
        PublishedPrimaryColorHex = DraftPrimaryColorHex;
        PublishedSecondaryColorHex = DraftSecondaryColorHex;
        PublishedAccentColorHex = DraftAccentColorHex;
        PublishedMenuLayoutMode = DraftMenuLayoutMode;
        PublishedOperatingHoursJson = DraftOperatingHoursJson;
        PublishedAnnouncementText = DraftAnnouncementText;
        PublishedWhatsAppNumber = DraftWhatsAppNumber;
        PublishedInstagramHandle = DraftInstagramHandle;

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
