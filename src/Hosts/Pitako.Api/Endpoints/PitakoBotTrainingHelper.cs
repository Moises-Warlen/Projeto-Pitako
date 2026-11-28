using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;

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
    public string EmployeeRoleTitle { get; set; } = "Atendente Virtual";
    public string EmployeeDirectives { get; set; } = string.Empty;
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
    IReadOnlyList<BotTrainingFaqEntry> Faqs,
    string EmployeeRoleTitle = "Atendente Virtual",
    string EmployeeDirectives = "");

internal sealed record BotSmartReplyResult(
    string ReplyText,
    string? MatchedFaqTitle,
    bool UsedTraining,
    bool UsedExternalLlm,
    string AiEngineLabel);

internal static class PitakoBotTrainingHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(14) };
    private const int MaxSettingValueLen = 1900;
    public const string GlobalAiSecretSettingKey = "platform.ai_api_key_protected";
    public const string GlobalAiMaskedSettingKey = "platform.ai_api_key_masked";

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
            string.IsNullOrWhiteSpace(profile.AssistantPersona) ? "Amigável e Acolhedor" : profile.AssistantPersona,
            profile.WelcomeGreeting,
            profile.StoreKnowledgeNotes,
            faqs,
            string.IsNullOrWhiteSpace(profile.EmployeeRoleTitle) ? "Atendente Virtual" : profile.EmployeeRoleTitle,
            profile.EmployeeDirectives ?? string.Empty);
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
            EmployeeRoleTitle = string.IsNullOrWhiteSpace(input.EmployeeRoleTitle) ? "Atendente Virtual" : input.EmployeeRoleTitle.Trim(),
            EmployeeDirectives = (input.EmployeeDirectives ?? string.Empty).Trim(),
            WelcomeGreeting = (input.WelcomeGreeting ?? string.Empty).Trim(),
            StoreKnowledgeNotes = (input.StoreKnowledgeNotes ?? string.Empty).Trim()
        };

        if (profile.WelcomeGreeting.Length > 350) profile.WelcomeGreeting = profile.WelcomeGreeting[..350];
        if (profile.StoreKnowledgeNotes.Length > 750) profile.StoreKnowledgeNotes = profile.StoreKnowledgeNotes[..750];
        if (profile.EmployeeDirectives.Length > 550) profile.EmployeeDirectives = profile.EmployeeDirectives[..550];

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

    public static async Task SaveEncryptedAiKeyAsync(
        IGlobalSettingRepository repo,
        IWhatsAppSecretProtector protector,
        Guid tenantId,
        string rawApiKey,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var protectedVal = protector.Protect(rawApiKey.Trim());
        var chunks = SplitChunks(protectedVal, 1800);
        await UpsertSettingAsync(repo, $"wa_ai_secret_meta:{tenantId:N}", chunks.Count.ToString(), $"Chave IA loja {tenantId:N}", utcNow, ct);
        for (var i = 0; i < chunks.Count; i++)
            await UpsertSettingAsync(repo, $"wa_ai_secret:{tenantId:N}:{i}", chunks[i], null, utcNow, ct);
        await repo.SaveChangesAsync(ct);
    }

    public static async Task<string?> ResolveEffectiveAiKeyAsync(
        IGlobalSettingRepository repo,
        IWhatsAppSecretProtector protector,
        IConfiguration? configuration,
        Guid tenantId,
        CancellationToken ct)
    {
        // 1. Store's own encrypted key (multi-chunk safe)
        var meta = await repo.FindByKeyAsync($"wa_ai_secret_meta:{tenantId:N}", ct);
        if (meta is not null && int.TryParse(meta.Value, out var count) && count > 0)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                var c = await repo.FindByKeyAsync($"wa_ai_secret:{tenantId:N}:{i}", ct);
                if (c is not null) sb.Append(c.Value);
            }
            if (sb.Length > 0)
            {
                try { return protector.Unprotect(sb.ToString()); } catch { }
            }
        }

        // 2. Global SaaS Platform AI Key (shared automatically with all existing & future stores)
        var globalSetting = await repo.FindByKeyAsync(GlobalAiSecretSettingKey, ct);
        if (globalSetting is not null && !string.IsNullOrWhiteSpace(globalSetting.Value))
        {
            try { return protector.Unprotect(globalSetting.Value); } catch { }
        }

        // 3. Server Environment fallback
        var envKey = configuration?["PITAKO_AI_API_KEY"]
            ?? configuration?["GEMINI_API_KEY"]
            ?? configuration?["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("PITAKO_AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return string.IsNullOrWhiteSpace(envKey) ? null : envKey.Trim();
    }

    public static async Task<bool> HasGlobalAiKeyAsync(
        IGlobalSettingRepository repo,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        var globalSetting = await repo.FindByKeyAsync(GlobalAiSecretSettingKey, ct);
        if (globalSetting is not null && !string.IsNullOrWhiteSpace(globalSetting.Value))
            return true;

        var envKey = configuration?["PITAKO_AI_API_KEY"]
            ?? configuration?["GEMINI_API_KEY"]
            ?? configuration?["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("PITAKO_AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return !string.IsNullOrWhiteSpace(envKey);
    }

    public static async Task<WhatsAppQrSessionState?> LoadQrSessionAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        CancellationToken ct)
    {
        var s = await repo.FindByKeyAsync($"wa_qr:{tenantId:N}", ct);
        if (s is null || string.IsNullOrWhiteSpace(s.Value)) return null;
        try { return JsonSerializer.Deserialize<WhatsAppQrSessionState>(s.Value, JsonOpts); }
        catch { return null; }
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
                items.Add(new BotFeedbackRecord(leg.Id, leg.CustomerPhone, "Cliente", leg.MessageText, leg.CreatedAtUtc, "Pending"));
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
                if (currentBatch.Count > 0) chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));
                currentBatch = [fb];
            }
        }
        if (currentBatch.Count > 0) chunks.Add(JsonSerializer.Serialize(currentBatch, JsonOpts));

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

    public static async Task<BotSmartReplyResult> GenerateAiEmployeeReplyAsync(
        string messageText,
        Tenant tenant,
        bool isOpenNow,
        string statusDetailText,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Product> activeProducts,
        IReadOnlyList<ModifierGroup> modifierGroups,
        DeliveryPolicy? deliveryPolicy,
        IReadOnlyList<Coupon> activeCoupons,
        Order? customerLastOrder,
        BotTrainingBundleDto training,
        IGlobalSettingRepository settingRepo,
        IWhatsAppSecretProtector protector,
        IConfiguration? configuration,
        IReadOnlyList<string>? recentHistoryLines,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var resolvedKey = await ResolveEffectiveAiKeyAsync(settingRepo, protector, configuration, tenant.Id, ct);
        if (!string.IsNullOrWhiteSpace(resolvedKey))
        {
            var systemPrompt = BuildFullStoreEmployeePrompt(
                tenant,
                isOpenNow,
                statusDetailText,
                categories,
                activeProducts,
                modifierGroups,
                deliveryPolicy,
                activeCoupons,
                customerLastOrder,
                training,
                utcNow);

            var (ok, llmReply, providerLabel) = await TryCallExternalLlmAsync(
                resolvedKey,
                systemPrompt,
                recentHistoryLines ?? [],
                messageText,
                ct);

            if (ok && !string.IsNullOrWhiteSpace(llmReply))
                return new BotSmartReplyResult(llmReply.Trim(), null, true, true, providerLabel);
        }

        // Local Smart Employee Engine with full catalog, sizes, modifiers, freight and order context
        var local = GenerateSmartBotReply(
            messageText,
            tenant,
            isOpenNow,
            statusDetailText,
            activeProducts,
            activeCoupons,
            training,
            utcNow,
            categories,
            modifierGroups,
            deliveryPolicy,
            customerLastOrder);

        return new BotSmartReplyResult(local.ReplyText, local.MatchedFaqTitle, local.UsedTraining, false, "Motor Local (Cardápio Completo)");
    }

    public static string BuildFullStoreEmployeePrompt(
        Tenant tenant,
        bool isOpenNow,
        string statusDetailText,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Product> activeProducts,
        IReadOnlyList<ModifierGroup> modifierGroups,
        DeliveryPolicy? deliveryPolicy,
        IReadOnlyList<Coupon> activeCoupons,
        Order? customerLastOrder,
        BotTrainingBundleDto training,
        DateTimeOffset utcNow)
    {
        var greeting = GetBrazilGreeting(utcNow);
        var catMap = categories.ToDictionary(c => c.Id, c => c.Name);
        var grpMap = modifierGroups.ToDictionary(g => g.Id, g => g);

        var sb = new StringBuilder();
        sb.AppendLine($"Você é o(a) {training.EmployeeRoleTitle} oficial da loja \"{tenant.TradeName}\" (segmento: {tenant.SegmentCode}).");
        sb.AppendLine($"Tom de voz desejado: {training.AssistantPersona}. Cumprimento atual pelo fuso do Brasil: \"{greeting}\".");
        sb.AppendLine("REGRAS ESTRITAS DE CONDUTA:");
        sb.AppendLine("1. Responda APENAS com base nos dados reais deste estabelecimento abaixo. Nunca invente produtos, preços, promoções, ingredientes, taxas ou prazos.");
        sb.AppendLine("2. Atue exatamente como um(a) funcionário(a) prestativo(a): tire dúvidas sobre o cardápio, ajude a montar/escolher tamanhos e adicionais, informe taxa de entrega, horários e formas de pagamento (PIX na tela, Cartão, Dinheiro) e informe o link do cardápio digital para finalizar o pedido.");
        sb.AppendLine($"3. Link oficial do cardápio desta loja: /loja/{tenant.Slug}");
        sb.AppendLine("4. Se o cliente pedir algo fora de contexto, com reclamação grave, ou pedir humano/gerente, oriente que está chamando um atendente humano.");
        if (!string.IsNullOrWhiteSpace(training.EmployeeDirectives))
            sb.AppendLine($"5. Diretrizes específicas desta loja: {training.EmployeeDirectives}");
        if (!string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes))
            sb.AppendLine($"Notas da loja: {training.StoreKnowledgeNotes}");

        sb.AppendLine();
        sb.AppendLine("--- DADOS DA LOJA ---");
        sb.AppendLine($"Status agora: {(isOpenNow ? "ABERTA" : $"FECHADA ({statusDetailText})")}");
        if (!string.IsNullOrWhiteSpace(tenant.Address)) sb.AppendLine($"Endereço: {tenant.Address}");
        if (!string.IsNullOrWhiteSpace(tenant.ContactPhone)) sb.AppendLine($"WhatsApp/Telefone: {tenant.ContactPhone}");

        if (deliveryPolicy is not null)
        {
            var freeText = deliveryPolicy.FreeShippingThreshold is not null
                ? $" | Frete grátis acima de R$ {deliveryPolicy.FreeShippingThreshold.Amount:0.00}"
                : string.Empty;
            sb.AppendLine($"Entrega/Frete: taxa base R$ {deliveryPolicy.BaseFee.Amount:0.00} (modo {deliveryPolicy.CalculationMode}), raio até {deliveryPolicy.MaxRadiusKm} km, prazo estimado {deliveryPolicy.BaseEstimatedMinutes} min, pedido mínimo R$ {deliveryPolicy.MinimumOrderAmount.Amount:0.00}{freeText}.");
        }

        if (activeCoupons.Count > 0)
            sb.AppendLine($"Cupons ativos: {string.Join(", ", activeCoupons.Select(c => c.Code))}.");

        if (customerLastOrder is not null)
            sb.AppendLine($"Último pedido deste cliente: {customerLastOrder.OrderNumber} | Status: {customerLastOrder.Status} | Total: R$ {customerLastOrder.TotalAmount.Amount:0.00} | Rastreio: /loja/{tenant.Slug}?pedido={customerLastOrder.OrderNumber}");

        sb.AppendLine();
        sb.AppendLine("--- CARDÁPIO COMPLETO DA LOJA ---");
        foreach (var prod in activeProducts.Take(60))
        {
            var catName = catMap.TryGetValue(prod.CategoryId, out var cn) ? cn : "Cardápio";
            var priceStr = prod.PromotionalPrice is not null
                ? $"de R$ {prod.BasePrice.Amount:0.00} por R$ {prod.PromotionalPrice.Amount:0.00}"
                : $"R$ {prod.BasePrice.Amount:0.00}";
            sb.Append($"• [{catName}] {prod.Name} ({priceStr})");
            if (!string.IsNullOrWhiteSpace(prod.Description)) sb.Append($" — {prod.Description}");

            if (prod.Variations.Count > 0)
            {
                var varsStr = string.Join(", ", prod.Variations.Where(v => v.IsActive).Select(v => $"{v.Name}: R$ {v.Price.Amount:0.00}"));
                if (!string.IsNullOrWhiteSpace(varsStr)) sb.Append($" | Tamanhos/Opções: {varsStr}");
            }

            if (prod.ModifierLinks.Count > 0)
            {
                var attachedNames = prod.ModifierLinks
                    .Select(l => grpMap.TryGetValue(l.ModifierGroupId, out var g) ? g.Name : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Take(5);
                sb.Append($" | Personalizações: {string.Join(", ", attachedNames)}");
            }
            sb.AppendLine();
        }

        if (modifierGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- COMPLEMENTOS / ADICIONAIS DISPONÍVEIS ---");
            foreach (var grp in modifierGroups.Take(15))
            {
                var opts = string.Join(", ", grp.Options.Take(20).Select(o =>
                    o.AdditionalPrice.Amount > 0 ? $"{o.Name} (+R$ {o.AdditionalPrice.Amount:0.00})" : o.Name));
                sb.AppendLine($"• {grp.Name}{(grp.FreeAllowance > 0 ? $" (até {grp.FreeAllowance} inclusos)" : "")}: {opts}");
            }
        }

        if (training.Faqs.Any(f => f.IsActive))
        {
            sb.AppendLine();
            sb.AppendLine("--- FAQS / REGRAS TREINADAS PELO DONO ---");
            foreach (var f in training.Faqs.Where(x => x.IsActive))
                sb.AppendLine($"• {f.QuestionTitle}: {f.AnswerText}");
        }

        return sb.ToString();
    }

    private static async Task<(bool Ok, string? Reply, string ProviderLabel)> TryCallExternalLlmAsync(
        string apiKey,
        string systemPrompt,
        IReadOnlyList<string> recentHistoryLines,
        string customerQuestion,
        CancellationToken ct)
    {
        try
        {
            var isOpenAi = apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
            var historyBlock = recentHistoryLines.Count > 0
                ? $"Histórico recente:\n{string.Join("\n", recentHistoryLines.TakeLast(6))}\n\nPergunta atual do cliente: {customerQuestion}"
                : customerQuestion;

            if (isOpenAi)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                req.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "gpt-4o-mini",
                    temperature = 0.35,
                    max_tokens = 450,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = historyBlock }
                    }
                }), Encoding.UTF8, "application/json");

                using var res = await SharedHttp.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) return (false, null, "OpenAI (Erro)");

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                return (!string.IsNullOrWhiteSpace(text), text, "OpenAI ChatGPT");
            }
            else
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={Uri.EscapeDataString(apiKey.Trim())}";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = historyBlock } } }
                    },
                    generationConfig = new { temperature = 0.35, maxOutputTokens = 450 }
                }), Encoding.UTF8, "application/json");

                using var res = await SharedHttp.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) return (false, null, "Gemini (Erro)");

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
                {
                    var text = cands[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    return (!string.IsNullOrWhiteSpace(text), text, "Google Gemini");
                }
                return (false, null, "Gemini");
            }
        }
        catch
        {
            return (false, null, "Fallback Local");
        }
    }

    public static (string ReplyText, string? MatchedFaqTitle, bool UsedTraining) GenerateSmartBotReply(
        string messageText,
        Tenant tenant,
        bool isOpenNow,
        string statusDetailText,
        IReadOnlyList<Product> activeProducts,
        IReadOnlyList<Coupon> activeCoupons,
        BotTrainingBundleDto training,
        DateTimeOffset? utcNow = null,
        IReadOnlyList<Category>? categories = null,
        IReadOnlyList<ModifierGroup>? modifierGroups = null,
        DeliveryPolicy? deliveryPolicy = null,
        Order? customerLastOrder = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var timeGreeting = GetBrazilGreeting(now);
        var lowerText = (messageText ?? string.Empty).Trim().ToLowerInvariant();
        var words = lowerText.Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        var rolePrefix = !string.IsNullOrWhiteSpace(training.EmployeeRoleTitle) ? $"({training.EmployeeRoleTitle}) " : string.Empty;

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

        // 2. Order tracking query
        if ((lowerText.Contains("pedido") || lowerText.Contains("status") || lowerText.Contains("cadê") || lowerText.Contains("cade") || lowerText.Contains("onde está")) && customerLastOrder is not null)
        {
            return ($"{timeGreeting}! Seu último pedido conosco é o {customerLastOrder.OrderNumber} no valor de R$ {customerLastOrder.TotalAmount.Amount:0.00} e está com status: {customerLastOrder.Status}. Você pode acompanhar ao vivo em: /loja/{tenant.Slug}?pedido={customerLastOrder.OrderNumber}", null, true);
        }

        // 3. Delivery fee / time / payment rules
        if (lowerText.Contains("entrega") || lowerText.Contains("taxa") || lowerText.Contains("frete") || lowerText.Contains("demora") || lowerText.Contains("tempo"))
        {
            var policyMsg = deliveryPolicy is not null
                ? $"{timeGreeting}! Aqui na {tenant.TradeName} entregamos em um raio de até {deliveryPolicy.MaxRadiusKm} km com tempo estimado de {deliveryPolicy.BaseEstimatedMinutes} min. A taxa base é R$ {deliveryPolicy.BaseFee.Amount:0.00}" +
                  (deliveryPolicy.FreeShippingThreshold is not null ? $" e o frete sai GRÁTIS em pedidos acima de R$ {deliveryPolicy.FreeShippingThreshold.Amount:0.00}!" : "!")
                : $"{timeGreeting}! Realizamos entregas na região e você confere a taxa exata ao preencher seu endereço no carrinho em /loja/{tenant.Slug}.";
            if (!string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes))
                policyMsg += $" {training.StoreKnowledgeNotes.Trim()}";
            return (policyMsg, null, !string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes));
        }

        // 4. Payment methods
        if (lowerText.Contains("pix") || lowerText.Contains("cartão") || lowerText.Contains("cartao") || lowerText.Contains("dinheiro") || lowerText.Contains("paga"))
        {
            var payMsg = $"{timeGreeting}! Na {tenant.TradeName} aceitamos PIX instantâneo na tela, Cartão de Crédito/Débito e Dinheiro (com campo para troco).";
            if (!string.IsNullOrWhiteSpace(training.EmployeeDirectives))
                payMsg += $" {training.EmployeeDirectives.Trim()}";
            return (payMsg, null, true);
        }

        // 5. Coupons / Promotions
        if (lowerText.Contains("cupom") || lowerText.Contains("desconto") || lowerText.Contains("promo"))
        {
            var promos = activeProducts.Where(p => p.PromotionalPrice is not null || (p.TagsCsv ?? "").Contains("promo", StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
            var promoPart = promos.Count > 0 ? $" Destaques em oferta: {string.Join(", ", promos.Select(p => $"{p.Name} (R$ {(p.PromotionalPrice ?? p.BasePrice).Amount:0.00})"))}" : "";
            var msg = activeCoupons.Count > 0
                ? $"{timeGreeting}! Aqui na {tenant.TradeName} temos o cupom ativo {activeCoupons[0].Code} para usar no carrinho.{promoPart}"
                : $"{timeGreeting}! Confira nossas ofertas e combos especiais direto no cardápio da {tenant.TradeName}!{promoPart}";
            return (msg, null, false);
        }

        // 6. Hours / Address
        if (lowerText.Contains("hora") || lowerText.Contains("abert") || lowerText.Contains("fech") || lowerText.Contains("onde") || lowerText.Contains("endere"))
        {
            var info = $"{timeGreeting}! Aqui na {tenant.TradeName} {(isOpenNow ? "estamos abertos agora e preparando pedidos!" : $"estamos fechados no momento ({statusDetailText}).")} {(string.IsNullOrWhiteSpace(tenant.Address) ? "" : $"Nosso endereço fica em {tenant.Address}.")}";
            if (!string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes))
                info += $" {training.StoreKnowledgeNotes.Trim()}";
            return (info.Trim(), null, !string.IsNullOrWhiteSpace(training.StoreKnowledgeNotes));
        }

        // 7. Modifier / Addon / Topping / Flavor search
        if (modifierGroups is { Count: > 0 } && words.Any(w => w.Length >= 4))
        {
            var matchedMods = modifierGroups
                .SelectMany(g => g.Options.Select(o => (Group: g, Option: o)))
                .Where(x => words.Any(w => w.Length >= 4 && (x.Option.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || x.Group.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
                .Take(5)
                .ToList();
            if (matchedMods.Count > 0)
            {
                var modList = string.Join(", ", matchedMods.Select(x =>
                    x.Option.AdditionalPrice.Amount > 0
                        ? $"{x.Option.Name} (+R$ {x.Option.AdditionalPrice.Amount:0.00})"
                        : $"{x.Option.Name} ({x.Group.Name})"));
                return ($"{timeGreeting}! Sim, trabalhamos com: {modList}. Você escolhe na hora de montar seu pedido em /loja/{tenant.Slug}!", null, false);
            }
        }

        // 8. Product / Category / Sizes search across full catalog
        var catNamesById = (categories ?? []).ToDictionary(c => c.Id, c => c.Name);
        var matchingProducts = activeProducts
            .Where(p => words.Any(w => w.Length >= 4 && (
                p.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                (p.Description ?? string.Empty).Contains(w, StringComparison.OrdinalIgnoreCase) ||
                (catNamesById.TryGetValue(p.CategoryId, out var cName) && cName.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                p.Variations.Any(v => v.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))))
            .Take(4)
            .ToList();

        if (matchingProducts.Count > 0)
        {
            var formattedMatches = string.Join("; ", matchingProducts.Select(p =>
            {
                if (p.Variations.Count > 0)
                {
                    var varList = string.Join(" / ", p.Variations.Where(v => v.IsActive).Take(4).Select(v => $"{v.Name}: R$ {v.Price.Amount:0.00}"));
                    return $"{p.Name} ({varList})";
                }
                return $"{p.Name} (R$ {p.BasePrice.Amount:0.00})";
            }));
            return ($"{timeGreeting}! Encontrei no nosso cardápio da {tenant.TradeName}: {formattedMatches}. Para montar ou pedir agora, acesse /loja/{tenant.Slug}!", null, false);
        }

        // 9. General welcoming employee showcase
        var showcase = activeProducts.Take(4).ToList();
        var itemsText = showcase.Count > 0
            ? string.Join(", ", showcase.Select(p => $"{p.Name} (R$ {p.BasePrice.Amount:0.00})"))
            : "todos os itens fresquinhos do nosso cardápio";

        var customGreeting = (training.WelcomeGreeting ?? string.Empty).Trim();
        var prefix = !string.IsNullOrWhiteSpace(customGreeting)
            ? (customGreeting.StartsWith("bom ", StringComparison.OrdinalIgnoreCase) || customGreeting.StartsWith("boa ", StringComparison.OrdinalIgnoreCase)
                ? customGreeting
                : $"{timeGreeting}! {customGreeting}")
            : $"{timeGreeting}! Aqui quem fala é o(a) {training.EmployeeRoleTitle} da {tenant.TradeName}! Que prazer atender você. Temos opções deliciosas como: {itemsText}";

        var directivePart = !string.IsNullOrWhiteSpace(training.EmployeeDirectives)
            ? $" {training.EmployeeDirectives.Trim()}"
            : string.Empty;

        var cta = training.AssistantPersona.Contains("Direto", StringComparison.OrdinalIgnoreCase)
            ? $"Peça direto em /loja/{tenant.Slug} ou digite \"atendente\"."
            : $"Você pode escolher tamanhos e adicionais em /loja/{tenant.Slug}, perguntar qualquer dúvida sobre sabores/frete/pedidos ou digitar \"atendente\" para falar com nossa equipe!";

        return ($"{prefix}.{directivePart} {cta}".Trim(), null, !string.IsNullOrWhiteSpace(customGreeting) || !string.IsNullOrWhiteSpace(directivePart));
    }

    private static List<string> SplitChunks(string input, int maxChunkSize)
    {
        var list = new List<string>();
        for (var i = 0; i < input.Length; i += maxChunkSize)
            list.Add(input.Substring(i, Math.Min(maxChunkSize, input.Length - i)));
        return list;
    }

    private static async Task UpsertSettingAsync(
        IGlobalSettingRepository repo,
        string key,
        string value,
        string? description,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var existing = await repo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var created = new GlobalPlatformSetting(Guid.NewGuid(), key, value, description, false);
            created.SetCreatedAudit(utcNow);
            await repo.AddAsync(created, ct);
        }
        else
        {
            existing.UpdateValue(value, description);
            existing.SetUpdatedAudit(utcNow);
        }
    }
}
