using System.Text.Json;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Catalog.Domain;

/// <summary>
/// Situação operacional de itens e categorias do cardápio (Seção 12).
/// Itens pausados ou arquivados não podem receber novos pedidos, preservando histórico.
/// </summary>
public enum CatalogItemStatus
{
    Active = 1,
    Paused = 2,
    Archived = 3
}

/// <summary>
/// 3 templates universais de categoria estilo Portal do Parceiro iFood:
/// Padrão (itens gerais), Montável (tamanhos + sabores/divisões) e Combos/Promoções.
/// </summary>
public enum CategoryTemplateKind
{
    Standard = 1,
    BuildYourOwn = 2,
    Combo = 3
}

public sealed record CatalogScheduleWindow(int DayOfWeek, bool IsAvailable, string StartTime, string EndTime);

public static class CatalogScheduleEvaluator
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static bool IsAvailableAt(string? scheduleJson, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(scheduleJson))
            return true;

        List<CatalogScheduleWindow>? windows;
        try
        {
            windows = JsonSerializer.Deserialize<List<CatalogScheduleWindow>>(scheduleJson, JsonOpts);
        }
        catch
        {
            return true;
        }

        if (windows is null || windows.Count == 0)
            return true;

        var brNow = utcNow.ToOffset(TimeSpan.FromHours(-3));
        var day = (int)brNow.DayOfWeek;
        var currentTime = TimeOnly.FromDateTime(brNow.DateTime);

        var dayEntries = windows.Where(w => w.DayOfWeek == day).ToList();
        if (dayEntries.Count == 0)
            return true;

        if (dayEntries.All(d => !d.IsAvailable))
            return false;

        foreach (var entry in dayEntries.Where(d => d.IsAvailable))
        {
            if (TimeOnly.TryParse(entry.StartTime, out var start) && TimeOnly.TryParse(entry.EndTime, out var end))
            {
                if (end <= start)
                {
                    if (currentTime >= start || currentTime <= end)
                        return true;
                }
                else if (currentTime >= start && currentTime <= end)
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Categoria ordenável de produtos pertencente exclusivamente a um estabelecimento.
/// </summary>
public sealed class Category : TenantScopedAggregateRoot
{
    private Category()
    {
        Name = string.Empty;
        TemplateKind = CategoryTemplateKind.Standard;
    }

    private Category(
        Guid id,
        Guid tenantId,
        string name,
        string? description,
        string? imageUrl,
        int displayOrder,
        string? segmentTemplateCode = null,
        string? ruleNotes = null,
        CategoryTemplateKind templateKind = CategoryTemplateKind.Standard,
        string? availabilityScheduleJson = null) : base(id, tenantId)
    {
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        DisplayOrder = displayOrder;
        SegmentTemplateCode = segmentTemplateCode;
        RuleNotes = ruleNotes;
        TemplateKind = templateKind;
        AvailabilityScheduleJson = availabilityScheduleJson;
        Status = CatalogItemStatus.Active;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public int DisplayOrder { get; private set; }
    public CatalogItemStatus Status { get; private set; }
    public string? SegmentTemplateCode { get; private set; }
    public string? RuleNotes { get; private set; }
    public CategoryTemplateKind TemplateKind { get; private set; }
    public string? AvailabilityScheduleJson { get; private set; }

    public static Result<Category> Create(
        Guid tenantId,
        string name,
        string? description = null,
        string? imageUrl = null,
        int displayOrder = 0,
        string? segmentTemplateCode = null,
        string? ruleNotes = null,
        CategoryTemplateKind templateKind = CategoryTemplateKind.Standard,
        string? availabilityScheduleJson = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Category>(Error.Validation("Category.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(Error.Validation("Category.NameRequired", "Nome da categoria é obrigatório."));

        return Result.Success(new Category(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim(),
            displayOrder,
            string.IsNullOrWhiteSpace(segmentTemplateCode) ? null : segmentTemplateCode.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(ruleNotes) ? null : ruleNotes.Trim(),
            Enum.IsDefined(templateKind) ? templateKind : CategoryTemplateKind.Standard,
            string.IsNullOrWhiteSpace(availabilityScheduleJson) ? null : availabilityScheduleJson.Trim()));
    }

    public Result Update(
        string name,
        string? description,
        string? imageUrl,
        int displayOrder,
        string? segmentTemplateCode = null,
        string? ruleNotes = null,
        CategoryTemplateKind? templateKind = null,
        string? availabilityScheduleJson = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("Category.NameRequired", "Nome da categoria é obrigatório."));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        DisplayOrder = displayOrder;
        SegmentTemplateCode = string.IsNullOrWhiteSpace(segmentTemplateCode) ? null : segmentTemplateCode.Trim().ToLowerInvariant();
        RuleNotes = string.IsNullOrWhiteSpace(ruleNotes) ? null : ruleNotes.Trim();
        if (templateKind.HasValue && Enum.IsDefined(templateKind.Value))
            TemplateKind = templateKind.Value;
        if (availabilityScheduleJson is not null)
            AvailabilityScheduleJson = string.IsNullOrWhiteSpace(availabilityScheduleJson) ? null : availabilityScheduleJson.Trim();
        return Result.Success();
    }

    public bool IsAvailableAt(DateTimeOffset utcNow) => CatalogScheduleEvaluator.IsAvailableAt(AvailabilityScheduleJson, utcNow);
    public void ChangeOrder(int displayOrder) => DisplayOrder = displayOrder;
    public void Pause() => Status = CatalogItemStatus.Paused;
    public void Reactivate() => Status = CatalogItemStatus.Active;
    public void Archive() => Status = CatalogItemStatus.Archived;
}
