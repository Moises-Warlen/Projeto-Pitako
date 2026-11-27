using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pitako.Api.Bootstrap;
using Pitako.Api.Security;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Security.Infrastructure.Persistence;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Endpoints;

public static partial class PitakoStoreSuiteEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly string[] ProfanityTerms =
    [
        "porra", "caralho", "merda", "fdp", "puta", "desgraca", "desgraça", "arrombado", "vai se foder", "lixo"
    ];

    public static void MapStoreSuiteExtensions(RouteGroupBuilder v1)
    {
        MapCatalogKitAndBestSellersEndpoints(v1);
        MapWhatsAppChatAndBotEndpoints(v1);
        MapPrintingSuiteEndpoints(v1);
        MapReportsAndSettlementEndpoints(v1);
        MapSecurityAuditEndpoints(v1);
        MapOperationalSettingsEndpoints(v1);
        MapMediaAndSegmentArtEndpoints(v1);
        MapProductFullConfigAndQrPayEndpoints(v1);
    }

    private static void MapCatalogKitAndBestSellersEndpoints(RouteGroupBuilder v1)
    {
        v1.MapPost("/catalog/import-spreadsheet-kit", async (
            ImportKitReq req,
            ApiSecurityGuard guard,
            StoreStarterProvisioner starter,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.SegmentCode))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Catalog.SegmentRequired", "Informe o segmento da planilha de ficha técnica."));

            var summary = await starter.ImportSpreadsheetKitAsync(user!, req.SegmentCode.Trim(), ct);
            return Results.Ok(summary);
        });

        v1.MapGet("/inventory/technical-sheets/summary", async (
            ApiSecurityGuard guard,
            ITechnicalSheetRepository sheetRepo,
            IProductRepository productRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;

            var sheets = await sheetRepo.ListAllAsync(ct);
            var products = await productRepo.ListAllOrderedAsync(ct);
            var productMap = products.ToDictionary(p => p.Id);

            return Results.Ok(sheets.Where(s => s.IsActive).Select(s =>
            {
                productMap.TryGetValue(s.ProductId ?? Guid.Empty, out var prod);
                var sellingPrice = prod?.BasePrice.Amount ?? 0m;
                var cost = s.CostPerPortion;
                var profit = sellingPrice > 0m ? decimal.Round(sellingPrice - cost, 2, MidpointRounding.ToEven) : 0m;
                var marginPct = sellingPrice > 0m ? decimal.Round((profit / sellingPrice) * 100m, 1, MidpointRounding.ToEven) : 0m;
                var cmvPct = sellingPrice > 0m ? decimal.Round((cost / sellingPrice) * 100m, 1, MidpointRounding.ToEven) : 0m;

                return new
                {
                    s.Id,
                    s.ProductId,
                    productName = prod?.Name ?? s.Name,
                    productSellingPrice = sellingPrice,
                    s.Name,
                    s.YieldQuantity,
                    s.YieldUnitCode,
                    s.IngredientsTotalCost,
                    s.PackagingsTotalCost,
                    s.TotalCost,
                    s.CostPerPortion,
                    profitPerUnit = profit,
                    marginPercent = marginPct,
                    cmvPercent = cmvPct,
                    isLossAlert = sellingPrice > 0m && cost >= sellingPrice,
                    isLowMarginAlert = sellingPrice > 0m && marginPct < 25m,
                    s.Notes,
                    s.Version,
                    ingredients = s.Ingredients.Select(i => new
                    {
                        i.SupplyItemId,
                        name = i.SupplyItemNameSnapshot,
                        i.Quantity,
                        i.UnitCode,
                        i.LossPercent,
                        i.CalculatedCost
                    }),
                    packagings = s.Packagings.Select(p => new
                    {
                        p.SupplyItemId,
                        p.Description,
                        p.Quantity,
                        p.UnitCode,
                        p.CalculatedCost
                    })
                };
            }));
        });
    }

    private static void MapWhatsAppChatAndBotEndpoints(RouteGroupBuilder v1)
    {
        var wa = v1.MapGroup("/whatsapp");

        wa.MapGet("/dashboard", async (
            ApiSecurityGuard guard,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IGlobalSettingRepository settingRepo,
            ITenantRepository tenantRepo,
            ISecurityAuditRepository auditRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            var channels = await channelRepo.ListAllAsync(ct);
            var channel = channels.FirstOrDefault();
            var conversations = await convRepo.ListInboxAsync(ct);
            var ops = await LoadStoreOpsStateAsync(settingRepo, tenantId, tenant, ct);
            var botState = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);
            var audits = await auditRepo.ListByTenantAsync(tenantId, ct);

            var allMessages = conversations.SelectMany(c => c.Messages).ToList();
            var inboundCount = allMessages.Count(m => m.Direction == WhatsAppMessageDirection.Inbound);
            var aiCount = allMessages.Count(m => m.Direction == WhatsAppMessageDirection.OutboundAiGrounded);
            var outboundCount = allMessages.Count(m => m.Direction != WhatsAppMessageDirection.Inbound);
            var handoffCount = conversations.Count(c =>
                c.Status == WhatsAppConversationStatus.WaitingHuman ||
                c.Status == WhatsAppConversationStatus.AssignedHuman);
            var profanityCount = audits.Count(a => a.ActionDescription.Contains("ofensiv", StringComparison.OrdinalIgnoreCase)) + botState.ProfanityBlockedCount;

            return Results.Ok(new
            {
                channel = channel is null ? null : new
                {
                    channel.Id,
                    channel.StoreTradeName,
                    channel.DisplayPhoneNumber,
                    channel.PhoneNumberId,
                    autoReplyMode = channel.AutoReplyMode.ToString(),
                    channel.MenuUrl,
                    channel.IsActive,
                    channel.LastWebhookUtc
                },
                chatAgentName = ops.ChatAgentName,
                aiEnabled = channel?.AutoReplyMode != WhatsAppAutoReplyMode.TraditionalBotOnly && botState.AiEnabled,
                hasGeminiApiKey = !string.IsNullOrWhiteSpace(botState.GeminiApiKeyMasked),
                geminiApiKeyMasked = botState.GeminiApiKeyMasked,
                metrics = new
                {
                    messagesReceived = inboundCount,
                    aiReplies = aiCount,
                    messagesSent = outboundCount,
                    humanHandoffs = handoffCount,
                    profanityBlocked = profanityCount,
                    menuReturns = botState.MenuReturnCount,
                    customersInBase = conversations.Select(c => c.CustomerPhone).Distinct().Count(),
                    optOuts = botState.OptOutCount
                },
                conversations = conversations.Select(c => new
                {
                    c.Id,
                    c.CustomerName,
                    c.CustomerPhone,
                    status = c.Status.ToString(),
                    c.HumanHandoffReason,
                    c.LastMessagePreview,
                    c.UnreadCount,
                    c.StartedAtUtc,
                    c.LastInteractionAtUtc,
                    messages = c.Messages.OrderBy(m => m.SentAtUtc).Select(m => new
                    {
                        m.Id,
                        direction = m.Direction.ToString(),
                        m.ContentText,
                    deliveryStatus = m.DeliveryStatus.ToString(),
                        m.DeliveryError,
                        m.GroundedByFacts,
                        m.SentAtUtc
                    })
                }),
                campaigns = botState.Campaigns,
                weeklySchedule = new
                {
                    enabled = botState.WeeklyScheduleEnabled,
                    dayOfWeek = botState.WeeklyScheduleDay,
                    time = botState.WeeklyScheduleHour
                },
                feedbacks = botState.Feedbacks
            });
        });

        wa.MapPut("/weekly-schedule", async (
            UpdateWeeklyScheduleReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var botState = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);
            botState.WeeklyScheduleEnabled = req.Enabled;
            botState.WeeklyScheduleDay = string.IsNullOrWhiteSpace(req.DayOfWeek) ? "Sexta-feira" : req.DayOfWeek.Trim();
            botState.WeeklyScheduleHour = string.IsNullOrWhiteSpace(req.Time) ? "17:00" : req.Time.Trim();
            await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);
            return Results.Ok(new { saved = true });
        });

        wa.MapPost("/feedbacks/{id:guid}/resolve", async (
            Guid id,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var botState = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);
            botState.Feedbacks = botState.Feedbacks.Where(f => f.Id != id).ToList();
            await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);
            return Results.Ok(new { resolved = true });
        });

        wa.MapPut("/channel", async (
            UpdateWhatsAppChannelReq req,
            ApiSecurityGuard guard,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppSecretProtector protector,
            WhatsAppSupportService waSvc,
            IGlobalSettingRepository settingRepo,
            ITenantRepository tenantRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            var channels = await channelRepo.ListAllAsync(ct);
            var existing = channels.FirstOrDefault();

            var phoneDigits = NormalizeDigits(req.DisplayPhoneNumber ?? existing?.DisplayPhoneNumber ?? tenant?.ContactPhone ?? "5516991098863");
            if (string.IsNullOrWhiteSpace(phoneDigits)) phoneDigits = "5516991098863";

            if (existing is null)
            {
                var mode = req.AiEnabled ? WhatsAppAutoReplyMode.HybridBotThenAi : WhatsAppAutoReplyMode.TraditionalBotOnly;
                var cfgRes = await waSvc.ConfigureChannelAsync(
                    user,
                    tenant?.TradeName ?? "Loja",
                    $"+{phoneDigits}",
                    $"phone-{tenant?.Slug ?? tenantId.ToString("N")[..6]}",
                    $"waba-{tenant?.Slug ?? tenantId.ToString("N")[..6]}",
                    protector.Protect("sandbox-wa-token"),
                    protector.Protect("sandbox-wa-secret"),
                    "pitako-verify",
                    mode,
                    $"/loja/{tenant?.Slug}",
                    null,
                    ct);

                if (cfgRes.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(cfgRes.Error);
                existing = cfgRes.Value;
            }
            else
            {
                if (req.IsActive) existing.Activate();
                else existing.Deactivate();
                existing.SetAutoReplyMode(req.AiEnabled ? WhatsAppAutoReplyMode.HybridBotThenAi : WhatsAppAutoReplyMode.TraditionalBotOnly);
                await channelRepo.SaveChangesAsync(ct);
            }

            var ops = await LoadStoreOpsStateAsync(settingRepo, tenantId, tenant, ct);
            if (!string.IsNullOrWhiteSpace(req.ChatAgentName))
                ops.ChatAgentName = req.ChatAgentName.Trim();
            ops.StoreWhatsAppPhone = phoneDigits;
            await SaveStoreOpsStateAsync(settingRepo, tenantId, ops, clock.UtcNow, ct);

            var botState = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);
            botState.AiEnabled = req.AiEnabled;
            if (!string.IsNullOrWhiteSpace(req.GeminiApiKey) && !req.GeminiApiKey.Contains('*', StringComparison.Ordinal))
                botState.GeminiApiKeyMasked = MaskSecret(req.GeminiApiKey.Trim());
            await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);

            return Results.Ok(new { updated = true, existing.Id, existing.IsActive, botState.AiEnabled, botState.GeminiApiKeyMasked });
        });

        wa.MapPost("/conversations/{id:guid}/reply", async (
            Guid id,
            ReplyChatReq req,
            ApiSecurityGuard guard,
            WhatsAppSupportService waSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.MessageText))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Chat.MessageRequired", "Digite uma resposta antes de enviar."));

            var res = await waSvc.SendHumanReplyAsync(user!, id, req.MessageText.Trim(), ct);
            return res.IsSuccess ? Results.Ok(new { sent = true, res.Value.LastMessagePreview }) : ApiSecurityGuard.ProblemFromError(res.Error);
        });

        wa.MapPost("/conversations/{id:guid}/resolve", async (
            Guid id,
            ApiSecurityGuard guard,
            WhatsAppSupportService waSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var res = await waSvc.ResolveConversationAsync(user!, id, ct);
            return res.IsSuccess ? Results.Ok(new { resolved = true }) : ApiSecurityGuard.ProblemFromError(res.Error);
        });

        wa.MapPost("/test-message", async (
            SendTestWaReq req,
            ApiSecurityGuard guard,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            WhatsAppSupportService waSvc,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var channels = await channelRepo.ListAllAsync(ct);
            var channel = channels.FirstOrDefault();
            if (channel is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("WhatsApp.ChannelNotFound", "Configure o número do WhatsApp da loja primeiro."));

            var phone = NormalizeDigits(req.TargetPhone);
            if (string.IsNullOrWhiteSpace(phone) || phone.Length < 10)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("WhatsApp.InvalidPhone", "Informe DDD + número válido."));

            var existing = await convRepo.FindByCustomerPhoneAsync(channel.Id, phone, ct);
            if (existing is null)
            {
                var convRes = WhatsAppConversation.Start(user!.TenantId!.Value, channel.Id, phone, $"Teste ({phone})", clock.UtcNow);
                if (convRes.IsFailure) return ApiSecurityGuard.ProblemFromError(convRes.Error);
                existing = convRes.Value;
                existing.RecordInbound($"test-in-{Guid.NewGuid():N}", "Teste de conexão WhatsApp", clock.UtcNow);
                await convRepo.AddAsync(existing, ct);
                await convRepo.SaveChangesAsync(ct);
            }

            var sendRes = await waSvc.SendHumanReplyAsync(user!, existing.Id, req.MessageText.Trim(), ct);
            if (sendRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(sendRes.Error);

            var latestMsg = sendRes.Value.Messages.OrderByDescending(m => m.SentAtUtc).FirstOrDefault();
            return Results.Ok(new
            {
                conversationId = existing.Id,
                deliveredToExternalMeta = latestMsg?.DeliveryStatus == WhatsAppDeliveryStatus.SentToCloud,
                note = latestMsg?.DeliveryError ?? "Mensagem registrada no histórico da loja.",
                preview = req.MessageText.Trim()
            });
        });

        wa.MapPost("/campaigns", async (
            CreateCampaignReq req,
            ApiSecurityGuard guard,
            IWhatsAppConversationRepository convRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.MessageText))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Campaign.Required", "Título e texto da campanha são obrigatórios."));

            var tenantId = user!.TenantId!.Value;
            var conversations = await convRepo.ListInboxAsync(ct);
            var targetCount = Math.Max(0, conversations.Select(c => c.CustomerPhone).Distinct().Count());
            var state = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);

            state.Campaigns.Insert(0, new BotCampaignEntry(
                Guid.NewGuid(),
                req.Title.Trim(),
                req.MessageText.Trim(),
                targetCount,
                clock.UtcNow,
                "Pronta / Base LGPD"));
            if (state.Campaigns.Count > 12)
                state.Campaigns = state.Campaigns.Take(12).ToList();

            await SaveBotOpsStateAsync(settingRepo, tenantId, state, clock.UtcNow, ct);
            return Results.Ok(new { created = true, campaigns = state.Campaigns });
        });

        v1.MapGet("/public/store/{slug}/chat", async (
            string slug,
            string? phone,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IGlobalSettingRepository settingRepo,
            IStoreVisualIdentityRepository identityRepo,
            IClock clock,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var ops = await LoadStoreOpsStateAsync(settingRepo, tenant.Id, tenant, ct);
            var identity = await identityRepo.FindByTenantIdForPublicViewAsync(tenant.Id, ct);
            var pub = identity?.GetPublishedView();
            var opStatus = StoreOperatingHoursHelper.Evaluate(pub?.OperatingHoursJson, pub?.IsTemporarilyPaused ?? false, pub?.PauseReason, clock.UtcNow);

            var cleanPhone = NormalizeDigits(phone);
            var channels = await channelRepo.ListAllAsync(ct);
            var channel = channels.FirstOrDefault();
            var conv = channel is not null && !string.IsNullOrWhiteSpace(cleanPhone)
                ? await convRepo.FindByCustomerPhoneAsync(channel.Id, cleanPhone, ct)
                : null;

            var messagesDto = conv is null
                ? []
                : conv.Messages.OrderBy(m => m.SentAtUtc).Select(m => new ChatMessageDto(m.Id, m.Direction.ToString(), m.ContentText, m.SentAtUtc)).ToList();

            return Results.Ok(new
            {
                agentName = ops.ChatAgentName,
                storeName = tenant.TradeName,
                isOpen = opStatus.IsOpenNow,
                statusBadge = opStatus.StatusLabel,
                messages = messagesDto
            });
        });

        v1.MapPost("/public/store/{slug}/chat", async (
            string slug,
            PublicChatMessageReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IGlobalSettingRepository settingRepo,
            IStoreVisualIdentityRepository identityRepo,
            IProductRepository productRepo,
            ICouponRepository couponRepo,
            ISecurityAuditRepository auditRepo,
            SecurityDbContext secDb,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            if (string.IsNullOrWhiteSpace(req.Message))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Chat.Empty", "Digite sua mensagem."));

            var cleanPhone = NormalizeDigits(req.CustomerPhone);
            if (string.IsNullOrWhiteSpace(cleanPhone))
                cleanPhone = $"web-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var channels = await channelRepo.ListAllAsync(ct);
            var channel = channels.FirstOrDefault();
            if (channel is null)
            {
                var chRes = WhatsAppChannelConfig.Create(
                    tenant.Id,
                    tenant.TradeName,
                    tenant.ContactPhone ?? "+5516991098863",
                    $"phone-{tenant.Slug}",
                    $"waba-{tenant.Slug}",
                    "sandbox-token",
                    "sandbox-secret",
                    "pitako-verify",
                    WhatsAppAutoReplyMode.HybridBotThenAi,
                    $"/loja/{tenant.Slug}");
                channel = chRes.Value;
                await channelRepo.AddAsync(channel, ct);
                await channelRepo.SaveChangesAsync(ct);
            }

            var textTrimmed = req.Message.Trim();
            var lowerText = textTrimmed.ToLowerInvariant();
            var botState = await LoadBotOpsStateAsync(settingRepo, tenant.Id, ct);

            if (ContainsProfanity(textTrimmed))
            {
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "WebChat";
                await auditRepo.AddAsync(new SecurityAuditLog(
                    Guid.NewGuid(),
                    SecurityEventType.CrossTenantAttemptBlocked,
                    UserScope.Establishment,
                    tenant.Id,
                    null,
                    null,
                    cleanPhone,
                    ip,
                    false,
                    $"Linguagem ofensiva bloqueada no chat web do remetente {cleanPhone}.",
                    clock.UtcNow), ct);
                await secDb.SaveChangesAsync(ct);

                botState.ProfanityBlockedCount += 1;
                await SaveBotOpsStateAsync(settingRepo, tenant.Id, botState, clock.UtcNow, ct);
            }

            var isComplaint = lowerText.Contains("reclam") || lowerText.Contains("atras") || lowerText.Contains("errado") || lowerText.Contains("ruim") || lowerText.Contains("demor");
            if (isComplaint)
            {
                botState.Feedbacks.Insert(0, new BotFeedbackEntry(Guid.NewGuid(), cleanPhone, textTrimmed, clock.UtcNow));
                if (botState.Feedbacks.Count > 20)
                    botState.Feedbacks = botState.Feedbacks.Take(20).ToList();
                await SaveBotOpsStateAsync(settingRepo, tenant.Id, botState, clock.UtcNow, ct);
            }

            var ops = await LoadStoreOpsStateAsync(settingRepo, tenant.Id, tenant, ct);
            var identity = await identityRepo.FindByTenantIdForPublicViewAsync(tenant.Id, ct);
            var pub = identity?.GetPublishedView();
            var opStatus = StoreOperatingHoursHelper.Evaluate(pub?.OperatingHoursJson, pub?.IsTemporarilyPaused ?? false, pub?.PauseReason, clock.UtcNow);

            var conv = await convRepo.FindByCustomerPhoneAsync(channel.Id, cleanPhone, ct);
            if (conv is null)
            {
                var st = WhatsAppConversation.Start(tenant.Id, channel.Id, cleanPhone, string.IsNullOrWhiteSpace(req.CustomerName) ? "Cliente Vitrine" : req.CustomerName.Trim(), clock.UtcNow);
                conv = st.Value;
                await convRepo.AddAsync(conv, ct);
            }
            conv.RecordInbound($"web-{Guid.NewGuid():N}", textTrimmed, clock.UtcNow);

            if (ContainsProfanity(textTrimmed))
            {
                conv.RecordOutbound(WhatsAppMessageDirection.OutboundTraditionalBot, null, "Por favor, mantenha o respeito no atendimento. Nossa equipe está pronta para ajudar de forma cordial!", true, null, true, clock.UtcNow);
            }
            else if (!opStatus.IsOpenNow && ops.AutoReplyWhenClosedEnabled && !string.IsNullOrWhiteSpace(ops.AutoReplyWhenClosedMessage))
            {
                var formatted = ops.AutoReplyWhenClosedMessage.Replace("{HORARIO}", opStatus.DetailText, StringComparison.OrdinalIgnoreCase);
                conv.RecordOutbound(WhatsAppMessageDirection.OutboundAiGrounded, null, formatted, true, null, true, clock.UtcNow);
            }
            else if (lowerText.Contains("atendente") || lowerText.Contains("humano") || lowerText.Contains("gerente") || isComplaint)
            {
                conv.RequestHumanHandoff(isComplaint ? "Ouvidoria / Reclamação registrada no chat" : "Cliente solicitou atendimento humano", clock.UtcNow);
                conv.RecordOutbound(
                    WhatsAppMessageDirection.OutboundTraditionalBot,
                    null,
                    $"Encaminhei sua mensagem para a equipe da {tenant.TradeName}! Um atendente responderá aqui mesmo em instantes.",
                    true,
                    null,
                    true,
                    clock.UtcNow);
            }
            else if (botState.AiEnabled && channel.IsActive && channel.AutoReplyMode != WhatsAppAutoReplyMode.HumanOnly)
            {
                var allProducts = (await productRepo.ListAllOrderedAsync(ct))
                    .Where(p => p.Status == CatalogItemStatus.Active)
                    .ToList();
                var activeCoupons = (await couponRepo.ListAllAsync(ct)).Where(c => c.IsActive).ToList();

                string replyText;
                if (lowerText.Contains("cupom") || lowerText.Contains("desconto") || lowerText.Contains("promo"))
                {
                    replyText = activeCoupons.Count > 0
                        ? $"Temos cupom ativo hoje na {tenant.TradeName}: use {activeCoupons[0].Code} no carrinho! Confira também nossos destaques no cardápio digital."
                        : $"Nossos combos e destaques já estão com valores especiais no cardápio da {tenant.TradeName}!";
                }
                else if (lowerText.Contains("hora") || lowerText.Contains("abert") || lowerText.Contains("fech") || lowerText.Contains("onde") || lowerText.Contains("endere"))
                {
                    replyText = $"{tenant.TradeName}: {(opStatus.IsOpenNow ? "estamos ABERTOS agora!" : opStatus.DetailText)} {(string.IsNullOrWhiteSpace(tenant.Address) ? "" : $"Endereço: {tenant.Address}.")}";
                }
                else
                {
                    var matchingProducts = allProducts
                        .Where(p => lowerText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length >= 4 && p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        .Take(3)
                        .ToList();
                    var showcase = matchingProducts.Count > 0 ? matchingProducts : allProducts.Take(4).ToList();
                    var itemsText = showcase.Count > 0
                        ? string.Join(" | ", showcase.Select(p => $"{p.Name} (R$ {p.BasePrice.Amount:0.00})"))
                        : "Confira todos os itens disponíveis acima no cardápio!";
                    replyText = $"Olá! Na {tenant.TradeName} temos: {itemsText}. Você pode personalizar tamanhos e ingredientes clicando no item ou, se preferir falar com alguém da equipe, digite \"atendente\".";
                }

                conv.RecordOutbound(WhatsAppMessageDirection.OutboundAiGrounded, null, replyText, true, null, true, clock.UtcNow);
            }

            await convRepo.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                conversationId = conv.Id,
                agentName = ops.ChatAgentName,
                messages = conv.Messages.OrderBy(m => m.SentAtUtc).Select(m => new ChatMessageDto(m.Id, m.Direction.ToString(), m.ContentText, m.SentAtUtc))
            });
        });
    }

    private static void MapPrintingSuiteEndpoints(RouteGroupBuilder v1)
    {
        var printing = v1.MapGroup("/printing");

        printing.MapGet("/printers", async (
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            ISectorRoutingRuleRepository routingRepo,
            IPrintJobRepository jobRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            var printers = await printerRepo.ListAllAsync(ct);
            var printerMap = printers.ToDictionary(p => p.Id);
            var rules = await routingRepo.ListActiveAsync(ct);
            var jobs = await jobRepo.ListRecentAsync(15, ct);

            return Results.Ok(new
            {
                printers = printers.Select(p =>
                {
                    var matchingRule = rules.FirstOrDefault(r => r.PrimaryPrinterId == p.Id);
                    var sectorLabel = matchingRule?.SectorType.ToString() ?? (p.PrinterCode.Contains("COZINHA", StringComparison.OrdinalIgnoreCase) ? "Kitchen" : "Counter");
                    return new
                    {
                        p.Id,
                        p.PrinterCode,
                        p.DisplayName,
                        sector = sectorLabel,
                        connectionKind = p.ConnectionKind.ToString(),
                        p.EndpointOrPort,
                        paperWidthMm = (int)p.PaperWidthMm,
                        status = p.Status.ToString(),
                        isAutoPrintEnabled = p.Status == PrinterStatus.Online
                    };
                }),
                recentJobs = jobs.Select(j =>
                {
                    printerMap.TryGetValue(j.ActualPrinterId ?? j.TargetPrinterId, out var targetPrinter);
                    return new
                    {
                        j.Id,
                        j.OrderOrTabReference,
                        printerDisplayName = targetPrinter?.DisplayName ?? j.SectorType.ToString(),
                        printerCode = targetPrinter?.PrinterCode ?? j.SectorType.ToString(),
                        status = j.Status.ToString(),
                        createdAtUtc = j.JobCreatedAtUtc
                    };
                })
            });
        });

        printing.MapPost("/printers/{id:guid}/toggle-auto", async (
            Guid id,
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            var printer = await printerRepo.FindByIdAsync(id, ct);
            if (printer is null || printer.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Printer.NotFound", "Impressora não encontrada."));

            var nextStatus = printer.Status == PrinterStatus.Online ? PrinterStatus.Paused : PrinterStatus.Online;
            printer.SetStatus(nextStatus);
            await printerRepo.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                printer.Id,
                printer.PrinterCode,
                status = printer.Status.ToString(),
                isAutoPrintEnabled = printer.Status == PrinterStatus.Online
            });
        });

        printing.MapPost("/printers/{id:guid}/test", async (
            Guid id,
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            IStorePrintIdentityProvider identityProvider,
            IThermalPrinterTransport transport,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            var printer = await printerRepo.FindByIdAsync(id, ct);
            if (printer is null || printer.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Printer.NotFound", "Impressora não encontrada."));

            var idRes = await identityProvider.GetStorePrintIdentityAsync(user.TenantId!.Value, ct);
            if (idRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(idRes.Error);

            var storeId = idRes.Value;
            var width = printer.ColumnWidth;
            var divider = new string('-', width);
            var sb = new StringBuilder();
            sb.AppendLine(CenterText(storeId.TradeName.ToUpperInvariant(), width));
            sb.AppendLine(CenterText($"CNPJ: {storeId.Cnpj.Formatted}", width));
            if (!string.IsNullOrWhiteSpace(storeId.Address))
                sb.AppendLine(CenterText(storeId.Address, width));
            sb.AppendLine(divider);
            sb.AppendLine($"TESTE DE IMPRESSAO - {printer.PrinterCode}");
            sb.AppendLine($"Equipamento: {printer.DisplayName}");
            sb.AppendLine($"Bobina: {(int)printer.PaperWidthMm}mm ({printer.EndpointOrPort})");
            sb.AppendLine($"Data/Hora: {clock.UtcNow.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy HH:mm}");
            sb.AppendLine(divider);
            sb.AppendLine("1x Item de Teste Operacional ....... R$ 24,90");
            sb.AppendLine("   Obs: Impressao termica configurada.");
            sb.AppendLine(divider);
            sb.AppendLine(CenterText("DOCUMENTO NAO FISCAL - CONFERENCIA", width));

            var text = sb.ToString();
            var bytes = Encoding.UTF8.GetBytes(text);
            var sendRes = await transport.SendEscPosAsync(printer, bytes, 1, ct);

            return Results.Ok(new
            {
                printer.Id,
                printer.PrinterCode,
                printer.DisplayName,
                paperWidthMm = (int)printer.PaperWidthMm,
                storeLogoUrl = storeId.LogoReference,
                storeTradeName = storeId.TradeName,
                storeCnpj = storeId.Cnpj.Formatted,
                hardwareDispatched = sendRes.IsSuccess,
                statusMessage = sendRes.IsSuccess
                    ? "Comando ESC/POS enviado à impressora."
                    : "Prévia térmica gerada com sucesso (sem spooler físico USB/TCP conectado neste host).",
                previewTicketText = text
            });
        });
    }

    private static void MapReportsAndSettlementEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/reports/executive", async (
            int? days,
            Guid? courierId,
            ApiSecurityGuard guard,
            IOrderRepository orderRepo,
            ITechnicalSheetRepository sheetRepo,
            IProductRepository productRepo,
            IDeliveryCourierRepository courierRepo,
            IDeliveryDispatchRunRepository runRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Reports, ct);
            if (err is not null) return err;

            var windowDays = Math.Clamp(days ?? 7, 1, 90);
            var cutoffUtc = clock.UtcNow.AddDays(-windowDays);

            var allOrders = (await orderRepo.ListRecentAllAsync(500, ct))
                .Where(o => o.PlacedAtUtc >= cutoffUtc)
                .ToList();

            var sheets = await sheetRepo.ListAllAsync(ct);
            var sheetByProduct = sheets.Where(s => s.IsActive && s.ProductId.HasValue)
                .GroupBy(s => s.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var validOrders = allOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
            var cancelledOrders = allOrders.Where(o => o.Status == OrderStatus.Cancelled).ToList();

            var netRevenue = validOrders.Sum(o => o.TotalAmount.Amount);
            var deliveryFees = validOrders.Sum(o => o.NetDeliveryFee.Amount);
            var discounts = validOrders.Sum(o => o.ItemsDiscount.Amount + o.DeliveryDiscount.Amount);
            var cancelledTotal = cancelledOrders.Sum(o => o.TotalAmount.Amount);
            var avgTicket = validOrders.Count > 0 ? decimal.Round(netRevenue / validOrders.Count, 2, MidpointRounding.ToEven) : 0m;

            decimal totalCogs = 0m;
            var productSales = new Dictionary<string, SalesBucket>(StringComparer.OrdinalIgnoreCase);
            var addOnCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var order in validOrders)
            {
                foreach (var item in order.Items)
                {
                    var unitCost = item.ProductId.HasValue && sheetByProduct.TryGetValue(item.ProductId.Value, out var sheet)
                        ? sheet.CostPerPortion
                        : decimal.Round(item.UnitPrice.Amount * 0.35m, 2, MidpointRounding.ToEven);
                    var lineCost = unitCost * item.Quantity;
                    totalCogs += lineCost;

                    var cur = productSales.TryGetValue(item.ItemTitle, out var prev) ? prev : new SalesBucket(0, 0m, 0m);
                    productSales[item.ItemTitle] = new SalesBucket(cur.Qty + item.Quantity, cur.Revenue + item.LineSubtotal.Amount, cur.Cost + lineCost);

                    if (!string.IsNullOrWhiteSpace(item.CustomizationSummaryText))
                    {
                        foreach (var rawPart in item.CustomizationSummaryText.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (rawPart.Length is > 2 and < 40)
                                addOnCounts[rawPart] = addOnCounts.GetValueOrDefault(rawPart) + item.Quantity;
                        }
                    }
                }
            }

            var products = await productRepo.ListAllOrderedAsync(ct);
            foreach (var prod in products)
            {
                if (!productSales.ContainsKey(prod.Name))
                    productSales[prod.Name] = new SalesBucket(0, 0m, 0m);
            }

            var grossProfit = decimal.Round(netRevenue - totalCogs, 2, MidpointRounding.ToEven);
            var finishedMinutes = validOrders
                .Where(o => o.CompletedAtUtc.HasValue)
                .Select(o => (o.CompletedAtUtc!.Value - o.PlacedAtUtc).TotalMinutes)
                .Where(m => m > 0 && m < 300)
                .ToList();
            var avgPrepMinutes = finishedMinutes.Count > 0 ? Math.Round(finishedMinutes.Average(), 0) : 24;

            var byHour = Enumerable.Range(0, 24)
                .Select(h => new
                {
                    hour = $"{h:00}h",
                    count = validOrders.Count(o => o.PlacedAtUtc.ToOffset(TimeSpan.FromHours(-3)).Hour == h)
                })
                .Where(x => x.count > 0)
                .OrderByDescending(x => x.count)
                .Take(8)
                .ToList();

            var paymentMethods = validOrders
                .GroupBy(o => string.IsNullOrWhiteSpace(o.PaymentMethod) ? "PIX" : o.PaymentMethod!)
                .Select(g => new { method = g.Key, count = g.Count(), total = g.Sum(x => x.TotalAmount.Amount) })
                .OrderByDescending(x => x.total)
                .ToList();

            var statusCounts = allOrders
                .GroupBy(o => o.Status.ToString())
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToList();

            var couriers = await courierRepo.ListAllAsync(ct);
            var runs = (await runRepo.ListRecentAsync(120, ct))
                .Where(r => r.AssignedAtUtc >= cutoffUtc)
                .ToList();
            if (courierId.HasValue && courierId.Value != Guid.Empty)
                runs = runs.Where(r => r.CourierId == courierId.Value).ToList();

            var courierMap = couriers.ToDictionary(c => c.Id);
            var orderMap = allOrders.ToDictionary(o => o.Id);

            decimal cashCollected = 0m;
            foreach (var r in runs)
            {
                foreach (var st in r.Stops)
                {
                    if (orderMap.TryGetValue(st.OrderId, out var ord) &&
                        (ord.PaymentMethod?.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        cashCollected += ord.TotalAmount.Amount;
                    }
                }
            }

            var totalFreightPayable = runs.Sum(r => r.TotalCommission.Amount);
            var totalStops = runs.Sum(r => r.Stops.Count);
            var totalDistanceKm = runs.Sum(r => r.TotalEstimatedDistanceKm);
            var settlementBalance = decimal.Round(totalFreightPayable - cashCollected, 2, MidpointRounding.ToEven);

            return Results.Ok(new
            {
                windowDays,
                financialKpis = new
                {
                    netRevenue,
                    validOrderCount = validOrders.Count,
                    deliveryFees,
                    discounts,
                    cancelledCount = cancelledOrders.Count,
                    cancelledTotal,
                    returnedCount = 0,
                    cogs = decimal.Round(totalCogs, 2, MidpointRounding.ToEven),
                    grossProfit,
                    avgTicket,
                    avgPrepMinutes
                },
                bestSellingProducts = productSales
                    .OrderByDescending(kv => kv.Value.Qty)
                    .ThenByDescending(kv => kv.Value.Revenue)
                    .Take(8)
                    .Select(kv => new { name = kv.Key, quantity = kv.Value.Qty, revenue = kv.Value.Revenue, cost = kv.Value.Cost }),
                leastSellingProducts = productSales
                    .OrderBy(kv => kv.Value.Qty)
                    .Take(6)
                    .Select(kv => new { name = kv.Key, quantity = kv.Value.Qty, revenue = kv.Value.Revenue }),
                topCustomizations = addOnCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .Select(kv => new { name = kv.Key, count = kv.Value }),
                peakHours = byHour,
                paymentMethods,
                statusCounts,
                courierSettlement = new
                {
                    couriers = couriers.Select(c => new
                    {
                        c.Id,
                        c.FullName,
                        c.Phone,
                        vehicleType = c.VehicleType.ToString(),
                        c.LicensePlate
                    }),
                    totalDeliveries = totalStops,
                    totalDistanceKm,
                    totalFreightPayable,
                    cashCollected,
                    settlementBalance,
                    runs = runs.Select(r => new
                    {
                        r.Id,
                        r.RunCode,
                        courierName = courierMap.TryGetValue(r.CourierId, out var c) ? c.FullName : "Entregador",
                        status = r.Status.ToString(),
                        r.TotalEstimatedDistanceKm,
                        freightCommission = r.TotalCommission.Amount,
                        r.AssignedAtUtc,
                        stops = r.Stops.OrderBy(s => s.StopSequence).Select(s => new
                        {
                            s.OrderNumberSnapshot,
                            s.CustomerNameSnapshot,
                            s.DestinationAddress,
                            s.LegDistanceKm,
                            commission = s.CourierCommission.Amount,
                            status = s.Status.ToString()
                        })
                    })
                }
            });
        });
    }

    private static void MapSecurityAuditEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/security/audit-logs", async (
            string? severity,
            ApiSecurityGuard guard,
            ISecurityAuditRepository auditRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Security, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var clearSetting = await settingRepo.FindByKeyAsync($"sec_clear:{tenantId:N}", ct);
            DateTimeOffset? clearedAfter = clearSetting is not null && DateTimeOffset.TryParse(clearSetting.Value, out var dt) ? dt : null;
            var logs = (await auditRepo.ListByTenantAsync(tenantId, ct))
                .Where(l => !clearedAfter.HasValue || l.OccurredAtUtc > clearedAfter.Value)
                .ToList();
            var cutoff24h = clock.UtcNow.AddHours(-24);

            var mapped = logs.OrderByDescending(l => l.OccurredAtUtc).Take(80).Select(l =>
            {
                var (sev, eventLabel, routeMethod) = l.EventType switch
                {
                    SecurityEventType.LoginLockedOut or SecurityEventType.LoginRateLimited => ("Critico", "Bloqueio Força Bruta (429)", "POST /api/v1/auth/login-store"),
                    SecurityEventType.LoginFailed => ("Alto", "Senha Incorreta (Login)", "POST /api/v1/auth/login-store"),
                    SecurityEventType.CrossTenantAttemptBlocked or SecurityEventType.UnauthorizedMasterAccessAttempt => ("Alto", l.ActionDescription.Contains("ofensiv", StringComparison.OrdinalIgnoreCase) ? "Linguagem Ofensiva Barrada" : "Acesso Não Autorizado (401/403)", "GUARD /api/v1"),
                    SecurityEventType.LoginSucceeded => ("Info", "Login Autorizado", "POST /api/v1/auth/login-store"),
                    _ => ("Medio", l.EventType.ToString(), "POST /api/v1/security")
                };

                return new
                {
                    l.Id,
                    l.OccurredAtUtc,
                    severity = sev,
                    eventType = eventLabel,
                    ipAddress = l.IpAddress ?? "127.0.0.1",
                    routeMethod,
                    details = l.ActionDescription,
                    l.Succeeded
                };
            }).ToList();

            var filtered = string.IsNullOrWhiteSpace(severity) || severity.Equals("Todos", StringComparison.OrdinalIgnoreCase)
                ? mapped
                : mapped.Where(x => x.severity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Ok(new
            {
                kpis = new
                {
                    criticalBlocks = mapped.Count(x => x.severity == "Critico"),
                    failedLogins = mapped.Count(x => x.eventType.Contains("Senha", StringComparison.OrdinalIgnoreCase)),
                    unauthorizedOrProfanity = mapped.Count(x => x.eventType.Contains("Não Autorizado", StringComparison.OrdinalIgnoreCase) || x.eventType.Contains("Ofensiva", StringComparison.OrdinalIgnoreCase)),
                    totalEvents24h = logs.Count(x => x.OccurredAtUtc >= cutoff24h)
                },
                events = filtered
            });
        });
    }

    private static void MapOperationalSettingsEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/store/operational-settings", async (
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            IStoreVisualIdentityRepository identityRepo,
            IDeliveryPolicyRepository deliveryRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            var identity = await identityRepo.FindCurrentTenantIdentityAsync(ct) ?? StoreVisualIdentity.CreateDefault(tenantId);
            var pub = identity.GetPublishedView();
            var opStatus = StoreOperatingHoursHelper.Evaluate(pub.OperatingHoursJson, pub.IsTemporarilyPaused, pub.PauseReason, clock.UtcNow);
            var policy = await deliveryRepo.FindEffectivePolicyAsync(null, ct);
            var ops = await LoadStoreOpsStateAsync(settingRepo, tenantId, tenant, ct);

            return Results.Ok(new
            {
                feePerKm = policy?.FeePerKm.Amount ?? 1.50m,
                minFreightFee = policy?.BaseFee.Amount ?? 5.00m,
                freeFreightUpToKm = policy?.IncludedKm ?? 1m,
                storeWhatsAppPhone = ops.StoreWhatsAppPhone ?? tenant.ContactPhone ?? "16991098863",
                chatAgentName = ops.ChatAgentName,
                storeAddress = tenant.Address ?? string.Empty,
                topBannerText = pub.AnnouncementText ?? pub.Slogan ?? $"Peça direto na {tenant.TradeName}!",
                autoOpenCloseBySchedule = ops.AutoOpenCloseBySchedule,
                isTemporarilyPaused = pub.IsTemporarilyPaused,
                schedule = opStatus.Schedule,
                autoReplyWhenClosedEnabled = ops.AutoReplyWhenClosedEnabled,
                autoReplyWhenClosedMessage = ops.AutoReplyWhenClosedMessage,
                pixKey = string.IsNullOrWhiteSpace(ops.PixKey) ? tenant.Cnpj.Formatted : ops.PixKey,
                mercadoPagoAccessTokenMasked = ops.MercadoPagoTokenMasked,
                hasMercadoPagoToken = !string.IsNullOrWhiteSpace(ops.MercadoPagoTokenMasked),
                whatsappGatewayUrl = ops.WhatsappGatewayUrl,
                whatsappGatewayTokenMasked = ops.WhatsappGatewayTokenMasked
            });
        });

        v1.MapPut("/store/operational-settings", async (
            UpdateOperationalSettingsReq req,
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            IStoreVisualIdentityRepository identityRepo,
            StoreBrandingService brandingSvc,
            IDeliveryPolicyRepository deliveryRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            await brandingSvc.UpdateStoreProfileAsync(
                user,
                tenantId,
                tenant.LegalName,
                tenant.TradeName,
                string.IsNullOrWhiteSpace(req.StoreAddress) ? tenant.Address : req.StoreAddress.Trim(),
                string.IsNullOrWhiteSpace(req.StoreWhatsAppPhone) ? tenant.ContactPhone : req.StoreWhatsAppPhone.Trim(),
                tenant.ContactEmail,
                ct);

            var identity = await identityRepo.FindCurrentTenantIdentityAsync(ct) ?? StoreVisualIdentity.CreateDefault(tenantId);
            var curDraft = identity.GetDraftPreview();
            var hoursJson = req.Schedule is { Count: 7 }
                ? JsonSerializer.Serialize(req.Schedule, JsonOpts)
                : curDraft.OperatingHoursJson;

            await brandingSvc.UpdateDraftAsync(
                user,
                tenantId,
                new UpdateStoreBrandingDraftCommand(
                    curDraft.LogoUrl,
                    curDraft.CoverImageUrl,
                    curDraft.Slogan,
                    curDraft.PrimaryColorHex,
                    curDraft.SecondaryColorHex,
                    curDraft.AccentColorHex,
                    curDraft.MenuLayoutMode,
                    curDraft.Banners,
                    hoursJson,
                    string.IsNullOrWhiteSpace(req.TopBannerText) ? curDraft.AnnouncementText : req.TopBannerText.Trim(),
                    req.StoreWhatsAppPhone ?? curDraft.WhatsAppNumber,
                    curDraft.InstagramHandle),
                ct);
            await brandingSvc.PublishBrandingAsync(user, tenantId, ct);

            var policy = await deliveryRepo.FindEffectivePolicyAsync(null, ct);
            if (policy is not null)
            {
                policy.UpdateConfiguration(
                    policy.OriginLatitude,
                    policy.OriginLongitude,
                    policy.MaxRadiusKm,
                    policy.CalculationMode,
                    Money.Create(Math.Max(0m, req.MinFreightFee), "BRL").Value,
                    Math.Max(0m, req.FreeFreightUpToKm),
                    Money.Create(Math.Max(0m, req.FeePerKm), "BRL").Value,
                    policy.BaseEstimatedMinutes,
                    policy.MinimumOrderAmount,
                    policy.FreeShippingThreshold,
                    policy.IsDeliveryEnabled,
                    policy.AllowPickup);
                await deliveryRepo.SaveChangesAsync(ct);
            }

            var ops = await LoadStoreOpsStateAsync(settingRepo, tenantId, tenant, ct);
            ops.StoreWhatsAppPhone = req.StoreWhatsAppPhone?.Trim() ?? ops.StoreWhatsAppPhone;
            ops.ChatAgentName = string.IsNullOrWhiteSpace(req.ChatAgentName) ? ops.ChatAgentName : req.ChatAgentName.Trim();
            ops.AutoOpenCloseBySchedule = req.AutoOpenCloseBySchedule;
            ops.AutoReplyWhenClosedEnabled = req.AutoReplyWhenClosedEnabled;
            if (!string.IsNullOrWhiteSpace(req.AutoReplyWhenClosedMessage))
                ops.AutoReplyWhenClosedMessage = req.AutoReplyWhenClosedMessage.Trim();
            if (!string.IsNullOrWhiteSpace(req.PixKey))
                ops.PixKey = req.PixKey.Trim();
            if (!string.IsNullOrWhiteSpace(req.MercadoPagoAccessToken) && !req.MercadoPagoAccessToken.Contains('*', StringComparison.Ordinal))
                ops.MercadoPagoTokenMasked = MaskSecret(req.MercadoPagoAccessToken.Trim());
            ops.WhatsappGatewayUrl = req.WhatsappGatewayUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(req.WhatsappGatewayToken) && !req.WhatsappGatewayToken.Contains('*', StringComparison.Ordinal))
                ops.WhatsappGatewayTokenMasked = MaskSecret(req.WhatsappGatewayToken.Trim());

            await SaveStoreOpsStateAsync(settingRepo, tenantId, ops, clock.UtcNow, ct);
            return Results.Ok(new { saved = true });
        });
    }

    private static async Task<StoreOpsState> LoadStoreOpsStateAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        Tenant? tenant,
        CancellationToken ct)
    {
        var key = $"store_ops:{tenantId:N}";
        var setting = await repo.FindByKeyAsync(key, ct);
        if (setting is not null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<StoreOpsState>(setting.Value, JsonOpts);
                if (parsed is not null) return parsed;
            }
            catch { }
        }

        return new StoreOpsState
        {
            ChatAgentName = tenant?.TradeName ?? "Atendimento",
            StoreWhatsAppPhone = tenant?.ContactPhone ?? "16991098863",
            AutoOpenCloseBySchedule = true,
            AutoReplyWhenClosedEnabled = true,
            AutoReplyWhenClosedMessage = $"Olá! Agradecemos sua mensagem. No momento a {tenant?.TradeName ?? "nossa loja"} está fechada ({{HORARIO}}). Seu recado foi registrado e responderemos assim que abrirmos!",
            PixKey = tenant?.Cnpj.Formatted ?? string.Empty
        };
    }

    private static async Task SaveStoreOpsStateAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        StoreOpsState state,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var key = $"store_ops:{tenantId:N}";
        var json = JsonSerializer.Serialize(state, JsonOpts);
        if (json.Length > 1950)
            json = json[..1950];

        var existing = await repo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var item = new GlobalPlatformSetting(Guid.NewGuid(), key, json, $"Configurações operacionais tenant {tenantId:N}", true);
            item.SetCreatedAudit(utcNow);
            await repo.AddAsync(item, ct);
        }
        else
        {
            existing.UpdateValue(json);
        }

        await repo.SaveChangesAsync(ct);
    }

    private static async Task<BotOpsState> LoadBotOpsStateAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        CancellationToken ct)
    {
        var key = $"wa_ops:{tenantId:N}";
        var setting = await repo.FindByKeyAsync(key, ct);
        if (setting is not null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<BotOpsState>(setting.Value, JsonOpts);
                if (parsed is not null) return parsed;
            }
            catch { }
        }

        return new BotOpsState { AiEnabled = true, MenuReturnCount = 0 };
    }

    private static async Task SaveBotOpsStateAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        BotOpsState state,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var key = $"wa_ops:{tenantId:N}";
        var json = JsonSerializer.Serialize(state, JsonOpts);
        if (json.Length > 1950)
            json = json[..1950];

        var existing = await repo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var item = new GlobalPlatformSetting(Guid.NewGuid(), key, json, $"Estado Bot IA WhatsApp tenant {tenantId:N}", true);
            item.SetCreatedAudit(utcNow);
            await repo.AddAsync(item, ct);
        }
        else
        {
            existing.UpdateValue(json);
        }

        await repo.SaveChangesAsync(ct);
    }

    private static string NormalizeDigits(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : NonDigitsRegex().Replace(raw, string.Empty);

    private static bool ContainsProfanity(string text)
    {
        var lower = text.ToLowerInvariant();
        return ProfanityTerms.Any(term => lower.Contains(term, StringComparison.Ordinal));
    }

    private static string MaskSecret(string secret) =>
        secret.Length <= 8 ? "********" : $"{secret[..4]}********{secret[^4..]}";

    private static string CenterText(string text, int width)
    {
        var clean = text.Length > width ? text[..width] : text;
        var pad = Math.Max(0, (width - clean.Length) / 2);
        return new string(' ', pad) + clean;
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigitsRegex();

    private sealed record ChatMessageDto(Guid Id, string Direction, string ContentText, DateTimeOffset SentAtUtc);
    private sealed record SalesBucket(int Qty, decimal Revenue, decimal Cost);

    private sealed class StoreOpsState
    {
        public string ChatAgentName { get; set; } = "Atendimento";
        public string? StoreWhatsAppPhone { get; set; }
        public bool AutoOpenCloseBySchedule { get; set; } = true;
        public bool AutoReplyWhenClosedEnabled { get; set; } = true;
        public string? AutoReplyWhenClosedMessage { get; set; }
        public string? PixKey { get; set; }
        public string? MercadoPagoTokenMasked { get; set; }
        public string? WhatsappGatewayUrl { get; set; }
        public string? WhatsappGatewayTokenMasked { get; set; }
    }

    private sealed class BotOpsState
    {
        public bool AiEnabled { get; set; } = true;
        public string? GeminiApiKeyMasked { get; set; }
        public int ProfanityBlockedCount { get; set; }
        public int MenuReturnCount { get; set; }
        public int OptOutCount { get; set; }
        public bool WeeklyScheduleEnabled { get; set; }
        public string WeeklyScheduleDay { get; set; } = "Sexta-feira";
        public string WeeklyScheduleHour { get; set; } = "17:00";
        public List<BotCampaignEntry> Campaigns { get; set; } = [];
        public List<BotFeedbackEntry> Feedbacks { get; set; } = [];
    }

    public sealed record BotCampaignEntry(Guid Id, string Title, string MessageText, int AudienceCount, DateTimeOffset CreatedAtUtc, string Status);
    public sealed record BotFeedbackEntry(Guid Id, string CustomerPhone, string MessageText, DateTimeOffset CreatedAtUtc);
    public sealed record UpdateWeeklyScheduleReq(bool Enabled, string DayOfWeek, string Time);
    public sealed record ImportKitReq(string SegmentCode);
    public sealed record UpdateWhatsAppChannelReq(string? DisplayPhoneNumber, bool IsActive = true, bool AiEnabled = true, string? GeminiApiKey = null, string? ChatAgentName = null);
    public sealed record ReplyChatReq(string MessageText);
    public sealed record SendTestWaReq(string TargetPhone, string MessageText);
    public sealed record CreateCampaignReq(string Title, string MessageText);
    public sealed record PublicChatMessageReq(string? CustomerName, string? CustomerPhone, string Message);
    public sealed record UpdateOperationalSettingsReq(
        decimal FeePerKm,
        decimal MinFreightFee,
        decimal FreeFreightUpToKm,
        string? StoreWhatsAppPhone,
        string? ChatAgentName,
        string? StoreAddress,
        string? TopBannerText,
        bool AutoOpenCloseBySchedule,
        IReadOnlyList<StoreOperatingHoursEntry>? Schedule,
        bool AutoReplyWhenClosedEnabled,
        string? AutoReplyWhenClosedMessage,
        string? PixKey,
        string? MercadoPagoAccessToken,
        string? WhatsappGatewayUrl,
        string? WhatsappGatewayToken);
}
