using System.Text.Json;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Tenants.Domain;

namespace Pitako.Api.Endpoints;

internal sealed record BotTrainingFaqEntry(
    Guid Id,
    string QuestionTitle,
    string KeywordsCsv,
    string AnswerText,
    bool IsActive = true);

internal sealed record BotFeedbackRecord(
    Guid Id,
    string CustomerPhone,
    string CustomerName,
    string MessageText,
    DateTimeOffset CreatedAtUtc,
    string Status = "Pending",
    DateTimeOffset? UpdatedAtUtc = null);

internal sealed class BotTrainingProfileState
{
    public string AssistantPersona { get; set; } = "Amigável e Acolhedor";
    public string WelcomeGreeting { get; set; } = string.Empty;
    public string StoreKnowledgeNotes { get; set; } = string.Empty;
}

internal sealed class WhatsAppQrSessionState
{
    public string SessionId { get; set; } = string.Empty;
    public string QrPayload { get; set; } = string.Empty;
    public string PairingCode { get; set; } = string.Empty;
    public string Status { get; set; } = "Disconnected";
    public string? PairedPhone { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}

internal sealed record BotTrainingBundleDto(
    string AssistantPersona,
    string WelcomeGreeting,
    string StoreKnowledgeNotes,
    IReadOnlyList<BotTrainingFaqEntry> Faqs);

internal static class PitakoBotTrainingHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const int MaxSettingValueLen = 1900;

    public static async Task<BotTrainingBundleDto> LoadTrainingAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        CancellationToken ct)
    {
        var profileSetting = await repo.FindByKeyAsync($"wa_train:{tenantId:N}", ct);
        BotTrainingProfileState profile = new();
        if (profileSetting is not null && !string.IsNullOrWhiteSpace(profileSetting.Value))
        {
            try
            {
                profile = JsonSerializer.Deserialize<BotTrainingProfileState>(profileSetting.Value, JsonOpts) ?? new();
            }
            catch { }
        }

        var faqs = new List<BotTrainingFaqEntry>();
        var allSettings = await repo.ListAllAsync(ct);
        var faqPrefix = $"wa_faq_chunk:{tenantId:N}:";
        foreach (var item in allSettings.Where(s => s.Key.StartsWith(faqPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Key))
        {
            try
            {
                var chunk = JsonSerializer.Deserialize<List<BotTrainingFaqEntry>>(item.Value, JsonOpts);
                if (chunk is { Count: > 0 }) faqs.AddRange(chunk);
            }
            catch { }
        }

        return new BotTrainingBundleDto(
            profile.AssistantPersona,
            profile.WelcomeGreeting,
            profile.StoreKnowledgeNotes,
            faqs);
    }

    public static async Task SaveTrainingAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        BotTrainingBundleDto input,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var profile = new BotTrainingProfileState
        {
            AssistantPersona = string.IsNullOrWhiteSpace(input.AssistantPersona) ? "Amigável e Acolhedor" : input.AssistantPersona.Trim(),
            WelcomeGreeting = (input.WelcomeGreeting ?? string.Empty).Trim(),
            StoreKnowledgeNotes = (input.StoreKnowledgeNotes ?? string.Empty).Trim()
        };

        // Keep profile well within 2000 char column limit
        if (profile.WelcomeGreeting.Length > 400) profile.WelcomeGreeting = profile.WelcomeGreeting[..400];
        if (profile.StoreKnowledgeNotes.Length > 1200) profile.StoreKnowledgeNotes = profile.StoreKnowledgeNotes[..1200];

        await UpsertSettingAsync(repo, $"wa_train:{tenantId:N}", JsonSerializer.Serialize(profile, JsonOpts), $"Treinamento IA tenant {tenantId:N}", utcNow, ct);

        var cleanedFaqs = (input.Faqs ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.QuestionTitle) && !string.IsNullOrWhiteSpace(f.AnswerText))
            .Select(f => f with
            {
                Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
                QuestionTitle = f.QuestionTitle.Trim(),
                KeywordsCsv = (f.KeywordsCsv ?? string.Empty).Trim(),
                AnswerText = f.AnswerText.Trim().Length > 600 ? f.AnswerText.Trim()[..600] : f.AnswerText.Trim()
            })
            .Take(40)
            .ToList();

        // Pack into <= 1850 char JSON chunks inside GlobalPlatformSetting rows
        var chunks = new List<string>();
        var currentBatch = new List<BotTrainingFaqEntry>();
        foreach (var faq in cleanedFaqs)
        {
            currentBatch.Add(faq);
            var candidate = JsonSerializer.Serialize(currentBatch, JsonOpts);
            if (candidate.Length > MaxSettingValueLen)
            {
                currentBatch.RemoveAt(currentBatch.Count - 1);
                if (currentBatch.Count > 0)
                    chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));
                currentBatch = [faq];
            }
        }
        if (currentBatch.Count > 0)
            chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));

        for (var i = 0; i < Math.Max(chunks.Count, 8); i++)
        {
            var chunkKey = $"wa_faq_chunk:{tenantId:N}:{i:00}";
            var payload = i < chunks.Count ? chunks[i] : "[]";
            if (i < chunks.Count || await repo.FindByKeyAsync(chunkKey, ct) is not null)
                await UpsertSettingAsync(repo, chunkKey, payload, $"FAQ Bot IA #{i + 1}", utcNow, ct);
        }

        await repo.SaveChangesAsync(ct);
    }

    public static async Task<WhatsAppQrSessionState?> LoadQrSessionAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        CancellationToken ct)
    {
        var s = await repo.FindByKeyAsync($"wa_qr:{tenantId:N}", ct);
        if (s is null || string.IsNullOrWhiteSpace(s.Value)) return null;
        try
        {
            return JsonSerializer.Deserialize<WhatsAppQrSessionState>(s.Value, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveQrSessionAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        WhatsAppQrSessionState qrState,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        await UpsertSettingAsync(repo, $"wa_qr:{tenantId:N}", JsonSerializer.Serialize(qrState, JsonOpts), $"Sessão QR WhatsApp tenant {tenantId:N}", utcNow, ct);
        await repo.SaveChangesAsync(ct);
    }

    public static async Task<List<BotFeedbackRecord>> LoadFeedbacksAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        IReadOnlyList<(Guid Id, string CustomerPhone, string MessageText, DateTimeOffset CreatedAtUtc)>? legacyItems,
        CancellationToken ct)
    {
        var items = new List<BotFeedbackRecord>();
        var allSettings = await repo.ListAllAsync(ct);
        var prefix = $"wa_fb_chunk:{tenantId:N}:";
        var chunkSettings = allSettings
            .Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Key)
            .ToList();

        if (chunkSettings.Count > 0)
        {
            foreach (var s in chunkSettings)
            {
                try
                {
                    var chunk = JsonSerializer.Deserialize<List<BotFeedbackRecord>>(s.Value, JsonOpts);
                    if (chunk is { Count: > 0 }) items.AddRange(chunk);
                }
                catch { }
            }
            return items.OrderByDescending(f => f.CreatedAtUtc).ToList();
        }

        if (legacyItems is { Count: > 0 })
        {
            foreach (var leg in legacyItems)
            {
                items.Add(new BotFeedbackRecord(
                    leg.Id,
                    leg.CustomerPhone,
                    "Cliente",
                    leg.MessageText,
                    leg.CreatedAtUtc,
                    "Pending"));
            }
        }

        return items.OrderByDescending(f => f.CreatedAtUtc).ToList();
    }

    public static async Task SaveFeedbacksAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        IReadOnlyList<BotFeedbackRecord> feedbacks,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var normalized = feedbacks
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f with
            {
                CustomerName = string.IsNullOrWhiteSpace(f.CustomerName) ? "Cliente" : f.CustomerName.Trim(),
                MessageText = f.MessageText.Trim().Length > 350 ? f.MessageText.Trim()[..350] : f.MessageText.Trim(),
                Status = string.IsNullOrWhiteSpace(f.Status) ? "Pending" : f.Status.Trim()
            })
            .Take(80)
            .ToList();

        var chunks = new List<string>();
        var currentBatch = new List<BotFeedbackRecord>();
        foreach (var fb in normalized)
        {
            currentBatch.Add(fb);
            var candidate = JsonSerializer.Serialize(currentBatch, JsonOpts);
            if (candidate.Length > MaxSettingValueLen)
            {
                currentBatch.RemoveAt(currentBatch.Count - 1);
                if (currentBatch.Count > 0)
                    chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));
                currentBatch = [fb];
            }
        }
        if (currentBatch.Count > 0)
            chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));

        for (var i = 0; i < Math.Max(chunks.Count, 8); i++)
        {
            var chunkKey = $"wa_fb_chunk:{tenantId:N}:{i:00}";
            var payload = i < chunks.Count ? chunks[i] : "[]";
            if (i < chunks.Count || await repo.FindByKeyAsync(chunkKey, ct) is not null)
                await UpsertSettingAsync(repo, chunkKey, payload, $"Ouvidoria WhatsApp #{i + 1}", utcNow, ct);
        }

        await repo.SaveChangesAsync(ct);
    }

    public static string GetBrazilGreeting(DateTimeOffset utcNow)
    {
        var localHour = utcNow.ToOffset(TimeSpan.FromHours(-3)).Hour;
        if (localHour is >= 5 and < 12) return "Bom dia";
        if (localHour is >= 12 and < 18) return "Boa tarde";
        return "Boa noite";
    }

    public static (string ReplyText, string? MatchedFaqTitle, bool UsedTraining) GenerateSmartBotReply(
        string messageText,
        Tenant tenant,
        bool isOpenNow,
        string statusDetailText,
        IReadOnlyList<Product> activeProducts,
        IReadOnlyList<Coupon> activeCoupons,
        BotTrainingBundleDto training,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var timeGreeting = GetBrazilGreeting(now);
        var lowerText = (messageText ?? string.Empty).Trim().ToLowerInvariant();
        var words = lowerText.Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries);

        // 1. Check trained FAQs by explicit keywords or question words
        foreach (var faq in training.Faqs.Where(f => f.IsActive))
        {
            var triggers = (faq.KeywordsCsv ?? string.Empty)
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .ToList();

            var keywordHit = triggers.Any(t => lowerText.Contains(t.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
            var titleWords = faq.QuestionTitle.ToLowerInvariant()
                .Split([' ', '?', '!', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 4)
                .ToList();
            var titleHit = titleWords.Count > 0 && titleWords.Count(w => lowerText.Contains(w, StringComparison.OrdinalIgnoreCase)) >= Math.Min(2, titleWords.Count);

            if (keywordHit || titleHit)
            {
                return ($"{timeGreeting}! {faq.AnswerText.Trim()}", faq.QuestionTitle, true);
            }
        }

        // 2. Coupons / Promotions
        if (lowerText.Contains("cupom") || lowerText.Contains("desconto") || lowerText.Contains("promo"))
        {
            var msg = activeCoupons.Count > 0
                ? $"{timeGreeting}! Que alegria falar com você! Aqui na {tenant.TradeName} temos o cupom especial {activeCoupons[0].Code} esperando por você no carrinho. Aproveite também nossos destaques no cardápio!"
                : $"{timeGreeting}! Nossos combos e destaques já estão com valores especiais no cardápio da {tenant.TradeName}!";
            return (msg, null, false);
        }

        // 3. Hours / Address / Trained Store Notes when asked about info/entrega/pagamento
        if (lowerText.Contains("hora") || lowerText.Contains("abert") || lowerText.Contains("fech") || lowerText.Contains("onde") || lowerText.Contains("endere"))
        {
            var info = $"{timeGreeting}! Aqui na {tenant.TradeName} {(isOpenNow ? "estamos abertos e prontos para preparar seu pedido!" : $"estamos fechados no momento ({statusDetailText}).")} {(string.IsNullOrWhiteSpace(tenant.Address) ? "" : $"Nosso endereço fica em {tenant.Address}.")}";
            if (!string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes))
                info += $" {training.StoreKnowledgeNotes.Trim()}";
            return (info.Trim(), null, !string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes));
        }

        // 4. Product search + Store Knowledge + Trained Greeting
        var matchingProducts = activeProducts
            .Where(p => words.Any(w => w.Length >= 4 && p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .ToList();
        var showcase = matchingProducts.Count > 0 ? matchingProducts : activeProducts.Take(4).ToList();
        var itemsText = showcase.Count > 0
            ? string.Join(", ", showcase.Select(p => $"{p.Name} (R$ {p.BasePrice.Amount:0.00})"))
            : "todos os sabores fresquinhos no cardápio acima";

        var customGreeting = (training.WelcomeGreeting ?? string.Empty).Trim();
        string prefix;
        if (!string.IsNullOrWhiteSpace(customGreeting))
        {
            var startsWithPeriod = customGreeting.StartsWith("bom dia", StringComparison.OrdinalIgnoreCase)
                || customGreeting.StartsWith("boa tarde", StringComparison.OrdinalIgnoreCase)
                || customGreeting.StartsWith("boa noite", StringComparison.OrdinalIgnoreCase);
            prefix = startsWithPeriod ? customGreeting : $"{timeGreeting}! {customGreeting}";
        }
        else
        {
            prefix = $"{timeGreeting}! Seja muito bem-vindo(a) à {tenant.TradeName}! Que prazer falar com você. Hoje temos opções deliciosas esperando por você, como: {itemsText}";
        }

        var itemsSegment = string.IsNullOrWhiteSpace(customGreeting) ? string.Empty : $" {itemsText}";

        var extraNotes = !string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes) &&
            (lowerText.Contains("entrega") || lowerText.Contains("taxa") || lowerText.Contains("frete") || lowerText.Contains("paga") || lowerText.Contains("pix"))
            ? $" {training.StoreKnowledgeNotes.Trim()}"
            : string.Empty;

        var cta = training.AssistantPersona.Contains("Direto", StringComparison.OrdinalIgnoreCase)
            ? "Escolha seu item no cardápio ou digite \"atendente\" para falar com nossa equipe."
            : "É só clicar no item desejado para personalizar tamanhos e acompanhamentos do seu jeitinho. Se preferir falar diretamente com alguém da nossa equipe, digite \"atendente\" que já te chamamos por aqui!";

        var finalReply = $"{prefix}{itemsSegment}.{extraNotes} {cta}".Trim();
        return (finalReply, null, !string.IsNullOrWhiteSpace(customGreeting) || !string.IsNullOrWhiteSpace(extraNotes));
    }

    private static async Task UpsertSettingAsync(
        IGlobalSettingRepository repo,
        string key,
        string value,
        string description,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var existing = await repo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var item = new GlobalPlatformSetting(Guid.NewGuid(), key, value, description, false);
            item.SetCreatedAudit(utcNow);
            await repo.AddAsync(item, ct);
        }
        else
        {
            existing.UpdateValue(value);
        }
    }
}
