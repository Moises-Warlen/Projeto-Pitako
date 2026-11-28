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
            var training = await PitakoBotTrainingHelper.LoadTrainingAsync(settingRepo, tenantId, ct);
            var qrSession = await PitakoBotTrainingHelper.LoadQrSessionAsync(settingRepo, tenantId, ct);
            var feedbacks = await PitakoBotTrainingHelper.LoadFeedbacksAsync(
                settingRepo,
                tenantId,
                botState.Feedbacks.Select(f => (f.Id, f.CustomerPhone, f.MessageText, f.CreatedAtUtc)).ToList(),
                ct);
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
                feedbacks,
                training,
                qrSession
            });
        });

        wa.MapPost("/qr-session", async (
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            var sessionId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var pairingCode = $"{sessionId[..4]}-{sessionId[4..8]}";
            var now = clock.UtcNow;
            var qrState = new WhatsAppQrSessionState
            {
                SessionId = sessionId,
                PairingCode = pairingCode,
                QrPayload = $"https://wa.me/{NormalizeDigits(tenant?.ContactPhone)}?text=Conectar-PITAKO-{tenant?.Slug ?? "loja"}-{pairingCode}",
                Status = "WaitingScan",
                PairedPhone = tenant?.ContactPhone,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddSeconds(120)
            };

            await PitakoBotTrainingHelper.SaveQrSessionAsync(settingRepo, tenantId, qrState, now, ct);
            return Results.Ok(qrState);
        });

        wa.MapPost("/qr-session/confirm", async (
            ConfirmQrReq? req,
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
            var now = clock.UtcNow;
            var channels = await channelRepo.ListAllAsync(ct);
            var existing = channels.FirstOrDefault();
            var rawPhone = NormalizeDigits(req?.DisplayPhoneNumber ?? existing?.DisplayPhoneNumber ?? tenant?.ContactPhone ?? "5516991098863");
            if (string.IsNullOrWhiteSpace(rawPhone)) rawPhone = "5516991098863";

            if (existing is null)
            {
                await waSvc.ConfigureChannelAsync(
                    user,
                    tenant?.TradeName ?? "Loja",
                    $"+{rawPhone}",
                    $"qr-{tenant?.Slug ?? tenantId.ToString("N")[..6]}",
                    $"waba-qr-{tenant?.Slug ?? tenantId.ToString("N")[..6]}",
                    protector.Protect("qr-session-token"),
                    protector.Protect("qr-session-secret"),
                    "pitako-qr-verify",
                    WhatsAppAutoReplyMode.HybridBotThenAi,
                    $"/loja/{tenant?.Slug}",
                    null,
                    ct);
            }
            else
            {
                existing.Activate();
                await channelRepo.SaveChangesAsync(ct);
            }

            var currentQr = await PitakoBotTrainingHelper.LoadQrSessionAsync(settingRepo, tenantId, ct) ?? new WhatsAppQrSessionState();
            currentQr.Status = "Connected";
            currentQr.PairedPhone = $"+{rawPhone}";
            if (string.IsNullOrWhiteSpace(currentQr.SessionId))
                currentQr.SessionId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            await PitakoBotTrainingHelper.SaveQrSessionAsync(settingRepo, tenantId, currentQr, now, ct);

            return Results.Ok(new { connected = true, qrSession = currentQr, displayPhoneNumber = $"+{rawPhone}" });
        });

        wa.MapPut("/training", async (
            SaveBotTrainingReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var bundle = new BotTrainingBundleDto(
                req.AssistantPersona ?? "Amigável e Acolhedor",
                req.WelcomeGreeting ?? string.Empty,
                req.StoreKnowledgeNotes ?? string.Empty,
                (req.Faqs ?? []).Select(f => new BotTrainingFaqEntry(f.Id, f.QuestionTitle, f.KeywordsCsv, f.AnswerText, f.IsActive)).ToList());

            await PitakoBotTrainingHelper.SaveTrainingAsync(settingRepo, tenantId, bundle, clock.UtcNow, ct);
            var saved = await PitakoBotTrainingHelper.LoadTrainingAsync(settingRepo, tenantId, ct);
            return Results.Ok(new { saved = true, training = saved });
        });

        wa.MapPost("/training/test", async (
            TestBotTrainingReq req,
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            IStoreVisualIdentityRepository identityRepo,
            IProductRepository productRepo,
            ICouponRepository couponRepo,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.Question))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Training.QuestionRequired", "Digite uma pergunta para testar o bot."));

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Tenant.NotFound", "Loja não encontrada."));

            var training = await PitakoBotTrainingHelper.LoadTrainingAsync(settingRepo, tenantId, ct);
            var identity = await identityRepo.FindByTenantIdForPublicViewAsync(tenantId, ct);
            var pub = identity?.GetPublishedView();
            var opStatus = StoreOperatingHoursHelper.Evaluate(pub?.OperatingHoursJson, pub?.IsTemporarilyPaused ?? false, pub?.PauseReason, clock.UtcNow);
            var allProducts = (await productRepo.ListAllOrderedAsync(ct)).Where(p => p.Status == CatalogItemStatus.Active).ToList();
            var activeCoupons = (await couponRepo.ListAllAsync(ct)).Where(c => c.IsActive).ToList();

            var (replyText, matchedFaqTitle, usedTraining) = PitakoBotTrainingHelper.GenerateSmartBotReply(
                req.Question,
                tenant,
                opStatus.IsOpenNow,
                opStatus.DetailText,
                allProducts,
                activeCoupons,
                training,
                clock.UtcNow);

            return Results.Ok(new { replyText, matchedFaqTitle, usedTraining });
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

        wa.MapPost("/feedbacks/{id:guid}/analyze", async (
            Guid id,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            ITenantRepository tenantRepo,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            var botState = await LoadBotOpsStateAsync(settingRepo, tenantId, ct);
            var list = await PitakoBotTrainingHelper.LoadFeedbacksAsync(
                settingRepo,
                tenantId,
                botState.Feedbacks.Select(f => (f.Id, f.CustomerPhone, f.MessageText, f.CreatedAtUtc)).ToList(),
                ct);

            var target = list.FirstOrDefault(f => f.Id == id);
            if (target is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Feedback.NotFound", "Ocorrência não encontrada."));

            list = list.Select(f => f.Id == id ? f with { Status = "Analyzing", UpdatedAtUtc = clock.UtcNow } : f).ToList();
            await PitakoBotTrainingHelper.SaveFeedbacksAsync(settingRepo, tenantId, list, clock.UtcNow, ct);
            if (botState.Feedbacks.Count > 0)
            {
                botState.Feedbacks = [];
                await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);
            }

            // Send notification message directly into the customer's chat conversation
            var channels = await channelRepo.ListAllAsync(ct);
            var channel = channels.FirstOrDefault();
            if (channel is not null && !string.IsNullOrWhiteSpace(target.CustomerPhone))
            {
                var conv = await convRepo.FindByCustomerPhoneAsync(channel.Id, target.CustomerPhone, ct);
                if (conv is null)
                {
                    var st = WhatsAppConversation.Start(tenantId, channel.Id, target.CustomerPhone, target.CustomerName, clock.UtcNow);
                    conv = st.Value;
                    await convRepo.AddAsync(conv, ct);
                }
                var greeting = PitakoBotTrainingHelper.GetBrazilGreeting(clock.UtcNow);
                var tradeName = tenant?.TradeName ?? "nossa loja";
                var snippet = target.MessageText.Length > 70 ? target.MessageText[..70] + "..." : target.MessageText;
                var notifyMsg = $"{greeting}! Aqui é da equipe da {tradeName}. Recebemos sua mensagem (\"{snippet}\") e já estamos analisando com atenção para te dar um retorno rápido!";
                conv.RecordOutbound(WhatsAppMessageDirection.OutboundHumanAgent, null, notifyMsg, true, null, true, clock.UtcNow);
                await convRepo.SaveChangesAsync(ct);
            }

            return Results.Ok(new { analyzing = true, id });
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
            var list = await PitakoBotTrainingHelper.LoadFeedbacksAsync(
                settingRepo,
                tenantId,
                botState.Feedbacks.Select(f => (f.Id, f.CustomerPhone, f.MessageText, f.CreatedAtUtc)).ToList(),
                ct);

            list = list.Select(f => f.Id == id ? f with { Status = "Resolved", UpdatedAtUtc = clock.UtcNow } : f).ToList();
            await PitakoBotTrainingHelper.SaveFeedbacksAsync(settingRepo, tenantId, list, clock.UtcNow, ct);
            if (botState.Feedbacks.Count > 0)
            {
                botState.Feedbacks = [];
                await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);
            }
            return Results.Ok(new { resolved = true });
        });

        wa.MapDelete("/feedbacks/{id:guid}", async (
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
            var list = await PitakoBotTrainingHelper.LoadFeedbacksAsync(
                settingRepo,
                tenantId,
                botState.Feedbacks.Select(f => (f.Id, f.CustomerPhone, f.MessageText, f.CreatedAtUtc)).ToList(),
                ct);

            list = list.Where(f => f.Id != id).ToList();
            await PitakoBotTrainingHelper.SaveFeedbacksAsync(settingRepo, tenantId, list, clock.UtcNow, ct);
            if (botState.Feedbacks.Count > 0)
            {
                botState.Feedbacks = [];
                await SaveBotOpsStateAsync(settingRepo, tenantId, botState, clock.UtcNow, ct);
            }
            return Results.Ok(new { deleted = true });
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

            var isComplaint = lowerText.Contains("reclam") || lowerText.Contains("atras") || lowerText.Contains("errado") || lowerText.Contains("ruim") || lowerText.Contains("demor") || lowerText.Contains("sugest") || lowerText.Contains("problema") || lowerText.Contains("faltou");
            if (isComplaint)
            {
                var fbList = await PitakoBotTrainingHelper.LoadFeedbacksAsync(
                    settingRepo,
                    tenant.Id,
                    botState.Feedbacks.Select(f => (f.Id, f.CustomerPhone, f.MessageText, f.CreatedAtUtc)).ToList(),
                    ct);
                fbList.Insert(0, new BotFeedbackRecord(
                    Guid.NewGuid(),
                    cleanPhone,
                    string.IsNullOrWhiteSpace(req.CustomerName) ? "Cliente" : req.CustomerName.Trim(),
                    textTrimmed,
                    clock.UtcNow,
                    "Pending"));
                await PitakoBotTrainingHelper.SaveFeedbacksAsync(settingRepo, tenant.Id, fbList, clock.UtcNow, ct);
                if (botState.Feedbacks.Count > 0)
                {
                    botState.Feedbacks = [];
                    await SaveBotOpsStateAsync(settingRepo, tenant.Id, botState, clock.UtcNow, ct);
                }
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
                var greeting = PitakoBotTrainingHelper.GetBrazilGreeting(clock.UtcNow);
                conv.RequestHumanHandoff(isComplaint ? "Ouvidoria / Reclamação registrada no chat" : "Cliente solicitou atendimento humano", clock.UtcNow);
                conv.RecordOutbound(
                    WhatsAppMessageDirection.OutboundTraditionalBot,
                    null,
                    $"{greeting}! Encaminhei sua mensagem para a equipe da {tenant.TradeName}. Um dos nossos atendentes já vai falar com você por aqui!",
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
                var training = await PitakoBotTrainingHelper.LoadTrainingAsync(settingRepo, tenant.Id, ct);

                var (replyText, _, _) = PitakoBotTrainingHelper.GenerateSmartBotReply(
                    textTrimmed,
                    tenant,
                    opStatus.IsOpenNow,
                    opStatus.DetailText,
                    allProducts,
                    activeCoupons,
                    training,
                    clock.UtcNow);

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
                        isPrimaryForSector = matchingRule is not null,
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

        printing.MapPost("/printers", async (
            CreatePrinterProfileReq req,
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            ISectorRoutingRuleRepository routingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.DisplayName) || string.IsNullOrWhiteSpace(req.EndpointOrPort))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Printer.FieldsRequired", "Informe o nome e a porta/IP da impressora."));

            var rawCode = string.IsNullOrWhiteSpace(req.PrinterCode)
                ? $"PRT-{req.Sector}-{DateTimeOffset.UtcNow:HHmmss}"
                : req.PrinterCode.Trim();
            var normalizedCode = new string(rawCode.ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            if (string.IsNullOrWhiteSpace(normalizedCode))
                normalizedCode = $"PRT-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            if (await printerRepo.FindByCodeAsync(normalizedCode, ct) is not null)
                normalizedCode = $"{normalizedCode[..Math.Min(28, normalizedCode.Length)]}-{DateTimeOffset.UtcNow:ss}";

            var connKind = Enum.TryParse<PrinterConnectionKind>(req.ConnectionKind, true, out var parsedConn)
                ? parsedConn
                : (req.EndpointOrPort.Contains(':') || req.EndpointOrPort.Contains('.')
                    ? PrinterConnectionKind.NetworkTcpEscPos
                    : PrinterConnectionKind.UsbSpooler);
            var paperWidth = req.PaperWidthMm == 58 ? PaperWidthMm.Mm58 : PaperWidthMm.Mm80;
            var sectorType = Enum.TryParse<ProductionSectorType>(req.Sector, true, out var parsedSec)
                ? parsedSec
                : ProductionSectorType.Kitchen;

            var createRes = PrinterProfile.Create(
                user!.TenantId!.Value,
                normalizedCode,
                req.DisplayName.Trim(),
                connKind,
                req.EndpointOrPort.Trim(),
                paperWidth);

            if (createRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(createRes.Error);

            var printer = createRes.Value;
            await printerRepo.AddAsync(printer, ct);

            var existingRule = await routingRepo.FindMatchingRuleAsync(sectorType, null, ct);
            if (req.SetAsPrimary || existingRule is null)
            {
                if (existingRule is null)
                {
                    var ruleRes = SectorRoutingRule.Create(user.TenantId!.Value, sectorType, printer.Id);
                    if (ruleRes.IsSuccess)
                        await routingRepo.AddAsync(ruleRes.Value, ct);
                }
                else
                {
                    existingRule.UpdatePrinters(printer.Id, existingRule.FallbackPrinterId, existingRule.CopiesCount);
                }
            }

            await printerRepo.SaveChangesAsync(ct);
            await routingRepo.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                printer.Id,
                printer.PrinterCode,
                printer.DisplayName,
                sector = sectorType.ToString(),
                isPrimaryForSector = req.SetAsPrimary || existingRule is null,
                connectionKind = printer.ConnectionKind.ToString(),
                printer.EndpointOrPort,
                paperWidthMm = (int)printer.PaperWidthMm
            });
        });

        printing.MapPost("/printers/{id:guid}/select", async (
            Guid id,
            string? sector,
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            ISectorRoutingRuleRepository routingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            var printer = await printerRepo.FindByIdAsync(id, ct);
            if (printer is null || printer.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Printer.NotFound", "Impressora não encontrada."));

            printer.SetStatus(PrinterStatus.Online);

            var rules = await routingRepo.ListActiveAsync(ct);
            var existingPrinterRule = rules.FirstOrDefault(r => r.PrimaryPrinterId == id);
            var sectorType = Enum.TryParse<ProductionSectorType>(sector, true, out var parsedSec)
                ? parsedSec
                : existingPrinterRule?.SectorType
                    ?? (printer.PrinterCode.Contains("COZINHA", StringComparison.OrdinalIgnoreCase)
                        ? ProductionSectorType.Kitchen
                        : ProductionSectorType.Counter);

            var targetRule = await routingRepo.FindMatchingRuleAsync(sectorType, null, ct);
            if (targetRule is null)
            {
                var ruleRes = SectorRoutingRule.Create(user.TenantId!.Value, sectorType, printer.Id);
                if (ruleRes.IsSuccess)
                    await routingRepo.AddAsync(ruleRes.Value, ct);
            }
            else
            {
                var backup = targetRule.FallbackPrinterId == printer.Id ? null : targetRule.FallbackPrinterId;
                targetRule.UpdatePrinters(printer.Id, backup, targetRule.CopiesCount);
            }

            await printerRepo.SaveChangesAsync(ct);
            await routingRepo.SaveChangesAsync(ct);

            return Results.Ok(new { selected = true, printer.Id, printer.DisplayName, sector = sectorType.ToString() });
        });

        printing.MapDelete("/printers/{id:guid}", async (
            Guid id,
            ApiSecurityGuard guard,
            IPrinterProfileRepository printerRepo,
            ISectorRoutingRuleRepository routingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null) return err;

            var printer = await printerRepo.FindByIdAsync(id, ct);
            if (printer is null || printer.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Printer.NotFound", "Impressora não encontrada."));

            await routingRepo.RemoveRulesForPrinterAsync(id, ct);
            await routingRepo.SaveChangesAsync(ct);

            printerRepo.Remove(printer);
            await printerRepo.SaveChangesAsync(ct);

            return Results.Ok(new { deleted = true, id });
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

        v1.MapPost("/orders/{id:guid}/print-ticket", async (
            Guid id,
            int? paperWidthMm,
            string? sector,
            Guid? printerId,
            ApiSecurityGuard guard,
            IOrderRepository orderRepo,
            IPrinterProfileRepository printerRepo,
            IStorePrintIdentityProvider identityProvider,
            IThermalPrinterTransport transport,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null) return err;

            var order = await orderRepo.FindByIdAsync(id, ct);
            if (order is null || order.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Order.NotFound", "Pedido não encontrado."));

            var idRes = await identityProvider.GetStorePrintIdentityAsync(user.TenantId!.Value, ct);
            if (idRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(idRes.Error);

            var printers = await printerRepo.ListAllAsync(ct);
            var explicitPrinter = printerId.HasValue ? printers.FirstOrDefault(p => p.Id == printerId.Value) : null;
            var widthMm = paperWidthMm == 58 || (paperWidthMm is null && explicitPrinter?.PaperWidthMm == PaperWidthMm.Mm58) ? 58 : 80;
            var cols = widthMm == 58 ? 32 : 48;
            var sec = Enum.TryParse<ProductionSectorType>(sector, true, out var parsedSec)
                ? parsedSec
                : ProductionSectorType.Kitchen;
            var docKind = sec == ProductionSectorType.Cashier
                ? PrintDocumentKind.OrderReceiptNonFiscal
                : order.FulfillmentMode == FulfillmentMode.Delivery
                    ? PrintDocumentKind.ProductionKitchenTicket
                    : PrintDocumentKind.CounterPickupConference;

            var fulfillmentLabel = order.FulfillmentMode switch
            {
                FulfillmentMode.Delivery => "Delivery",
                FulfillmentMode.CounterPickup => "Retirada Balcão",
                _ => "Mesa / Salão"
            };

            var ticketLines = order.Items.Select(i => new TicketLineInput(
                i.ItemTitle,
                i.Quantity,
                string.IsNullOrWhiteSpace(i.CustomizationSummaryText) ? "Padrão" : i.CustomizationSummaryText,
                i.CustomerNotes,
                i.LineSubtotal.Amount)).ToList();

            var payload = new ThermalTicketPayloadInput(
                sec,
                docKind,
                order.OrderNumber,
                $"{order.Channel} ({fulfillmentLabel})",
                string.IsNullOrWhiteSpace(order.CustomerName) ? "Consumidor" : order.CustomerName,
                order.FulfillmentMode == FulfillmentMode.DineIn ? "Mesa / Salão" : null,
                user.FullName,
                order.PlacedAtUtc,
                ticketLines,
                order.TotalAmount.Amount,
                1);

            var (renderedText, escPosBase64) = ThermalTicketRenderer.Render(idRes.Value, payload, cols);
            var divider = new string('-', cols);
            var extraBlock = new StringBuilder(renderedText);
            extraBlock.AppendLine($"Pagamento: {order.PaymentMethod} ({(order.IsPaymentConfirmed ? "CONFIRMADO" : "COBRAR")})");
            if (!string.IsNullOrWhiteSpace(order.CustomerPhone))
                extraBlock.AppendLine($"Fone: {order.CustomerPhone}");
            if (!string.IsNullOrWhiteSpace(order.DeliveryAddressSummary))
                extraBlock.AppendLine($"Entrega: {order.DeliveryAddressSummary}");
            if (order.NetDeliveryFee.Amount > 0m)
                extraBlock.AppendLine($"Frete: R$ {order.NetDeliveryFee.Amount:N2}");
            if (order.ItemsDiscount.Amount > 0m)
                extraBlock.AppendLine($"Desconto: -R$ {order.ItemsDiscount.Amount:N2}");
            extraBlock.AppendLine(divider);
            extraBlock.AppendLine($"Impresso em: {clock.UtcNow.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy HH:mm:ss}");

            var finalTicketText = extraBlock.ToString();
            var finalEscPos = Convert.ToBase64String(Encoding.UTF8.GetBytes(finalTicketText));

            var targetPrinter = explicitPrinter
                ?? printers
                    .Where(p => p.Status == PrinterStatus.Online)
                    .OrderByDescending(p => (int)p.PaperWidthMm == widthMm)
                    .FirstOrDefault();

            var hardwareDispatched = false;
            if (targetPrinter is not null)
            {
                var sendRes = await transport.SendEscPosAsync(targetPrinter, Encoding.UTF8.GetBytes(finalTicketText), 1, ct);
                hardwareDispatched = sendRes.IsSuccess;
            }

            return Results.Ok(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                paperWidthMm = widthMm,
                columnWidth = cols,
                storeLogoUrl = idRes.Value.LogoReference,
                storeTradeName = idRes.Value.TradeName,
                storeCnpj = idRes.Value.Cnpj.Formatted,
                storeAddress = idRes.Value.Address,
                storePhone = idRes.Value.Phone,
                hardwareDispatched,
                printerName = targetPrinter?.DisplayName,
                statusMessage = hardwareDispatched
                    ? $"Comanda {widthMm}mm enviada para {targetPrinter!.DisplayName}."
                    : $"Comanda {widthMm}mm pronta para impressão no navegador ou spooler ESC/POS.",
                previewTicketText = finalTicketText,
                escPosBase64 = finalEscPos
            });
        });
    }

    private static void MapReportsAndSettlementEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/reports/executive", async (
            int? days,
            string? startDate,
            string? endDate,
            Guid? courierId,
            string? runStatus,
            ApiSecurityGuard guard,
            IOrderRepository orderRepo,
            ITechnicalSheetRepository sheetRepo,
            IProductRepository productRepo,
            IDeliveryCourierRepository courierRepo,
            IDeliveryDispatchRunRepository runRepo,
            IGlobalSettingRepository settingRepo,
            ITenantRepository tenantRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Reports, ct);
            if (err is not null) return err;

            var brOffset = TimeSpan.FromHours(-3);
            var nowBr = clock.UtcNow.ToOffset(brOffset);
            var windowDays = Math.Clamp(days ?? 7, 1, 90);

            DateTimeOffset cutoffUtc;
            DateTimeOffset endUtc;
            string effectiveStartIso;
            string effectiveEndIso;

            if (DateTime.TryParse(startDate, out var parsedStart) && DateTime.TryParse(endDate, out var parsedEnd))
            {
                if (parsedEnd < parsedStart) (parsedStart, parsedEnd) = (parsedEnd, parsedStart);
                cutoffUtc = new DateTimeOffset(parsedStart.Year, parsedStart.Month, parsedStart.Day, 0, 0, 0, brOffset).ToUniversalTime();
                endUtc = new DateTimeOffset(parsedEnd.Year, parsedEnd.Month, parsedEnd.Day, 23, 59, 59, 999, brOffset).ToUniversalTime();
                effectiveStartIso = parsedStart.ToString("yyyy-MM-dd");
                effectiveEndIso = parsedEnd.ToString("yyyy-MM-dd");
                windowDays = Math.Max(1, (int)Math.Ceiling((parsedEnd - parsedStart).TotalDays) + 1);
            }
            else if (DateTime.TryParse(startDate, out var onlyStart))
            {
                cutoffUtc = new DateTimeOffset(onlyStart.Year, onlyStart.Month, onlyStart.Day, 0, 0, 0, brOffset).ToUniversalTime();
                endUtc = clock.UtcNow;
                effectiveStartIso = onlyStart.ToString("yyyy-MM-dd");
                effectiveEndIso = nowBr.ToString("yyyy-MM-dd");
            }
            else
            {
                cutoffUtc = clock.UtcNow.AddDays(-windowDays);
                endUtc = clock.UtcNow;
                effectiveStartIso = nowBr.AddDays(-windowDays + 1).ToString("yyyy-MM-dd");
                effectiveEndIso = nowBr.ToString("yyyy-MM-dd");
            }

            var tenant = user?.TenantId.HasValue == true
                ? await tenantRepo.FindByIdAsync(user.TenantId.Value, ct)
                : null;
            var ops = tenant is not null
                ? await LoadStoreOpsStateAsync(settingRepo, tenant.Id, tenant, ct)
                : null;

            var allOrders = (await orderRepo.ListRecentAllAsync(500, ct))
                .Where(o => o.PlacedAtUtc >= cutoffUtc && o.PlacedAtUtc <= endUtc)
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
            var runs = (await runRepo.ListRecentAsync(250, ct))
                .Where(r => r.AssignedAtUtc >= cutoffUtc && r.AssignedAtUtc <= endUtc)
                .ToList();
            if (courierId.HasValue && courierId.Value != Guid.Empty)
                runs = runs.Where(r => r.CourierId == courierId.Value).ToList();
            if (!string.IsNullOrWhiteSpace(runStatus) && !runStatus.Equals("All", StringComparison.OrdinalIgnoreCase))
                runs = runs.Where(r => r.Status.ToString().Equals(runStatus.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

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
                startDateIso = effectiveStartIso,
                endDateIso = effectiveEndIso,
                storeWhatsAppPhone = ops?.StoreWhatsAppPhone ?? tenant?.ContactPhone,
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
    public sealed record CreatePrinterProfileReq(string? PrinterCode, string DisplayName, string Sector, string? ConnectionKind, string EndpointOrPort, int PaperWidthMm = 80, bool SetAsPrimary = true);
    public sealed record ConfirmQrReq(string? DisplayPhoneNumber = null);
    public sealed record SaveBotTrainingReq(string? AssistantPersona, string? WelcomeGreeting, string? StoreKnowledgeNotes, IReadOnlyList<BotTrainingFaqReq>? Faqs);
    public sealed record BotTrainingFaqReq(Guid Id, string QuestionTitle, string KeywordsCsv, string AnswerText, bool IsActive = true);
    public sealed record TestBotTrainingReq(string Question);
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
