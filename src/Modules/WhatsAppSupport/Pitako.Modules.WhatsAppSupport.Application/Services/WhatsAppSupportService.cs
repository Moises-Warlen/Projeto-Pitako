using Pitako.Modules.Security.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.WhatsAppSupport.Application.Services;

/// <summary>
/// Serviço completo de WhatsApp Business Oficial, Inbox Humana, Bot Tradicional e Assistente IA
/// estritamente ancorado em dados verificados do estabelecimento (Seção 29).
/// </summary>
public sealed class WhatsAppSupportService
{
    private readonly IWhatsAppChannelConfigRepository _channelRepo;
    private readonly ITraditionalMenuBotRuleRepository _botRuleRepo;
    private readonly IWhatsAppConversationRepository _convRepo;
    private readonly IWhatsAppSecretProtector _secretProtector;
    private readonly IWhatsAppCloudGateway _cloudGateway;
    private readonly ITenantKnowledgeProvider _knowledgeProvider;
    private readonly IAiAssistantGateway _aiGateway;
    private readonly IInboxRealtimeNotifier _inboxNotifier;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public WhatsAppSupportService(
        IWhatsAppChannelConfigRepository channelRepo,
        ITraditionalMenuBotRuleRepository botRuleRepo,
        IWhatsAppConversationRepository convRepo,
        IWhatsAppSecretProtector secretProtector,
        IWhatsAppCloudGateway cloudGateway,
        ITenantKnowledgeProvider knowledgeProvider,
        IAiAssistantGateway aiGateway,
        IInboxRealtimeNotifier inboxNotifier,
        ITenantContext tenantContext,
        IClock clock)
    {
        _channelRepo = channelRepo;
        _botRuleRepo = botRuleRepo;
        _convRepo = convRepo;
        _secretProtector = secretProtector;
        _cloudGateway = cloudGateway;
        _knowledgeProvider = knowledgeProvider;
        _aiGateway = aiGateway;
        _inboxNotifier = inboxNotifier;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<WhatsAppChannelConfig>> ConfigureChannelAsync(
        User adminActor,
        string storeTradeName,
        string displayPhoneNumber,
        string phoneNumberId,
        string businessAccountId,
        string rawAccessToken,
        string rawAppSecret,
        string rawVerifyToken,
        WhatsAppAutoReplyMode autoReplyMode = WhatsAppAutoReplyMode.HybridBotThenAi,
        string? menuUrl = null,
        Guid? storeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(adminActor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<WhatsAppChannelConfig>(guard.Error);

        if (string.IsNullOrWhiteSpace(rawAccessToken) || string.IsNullOrWhiteSpace(rawAppSecret) || string.IsNullOrWhiteSpace(rawVerifyToken))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.SecretsRequired", "Token Cloud API, AppSecret e VerifyToken são obrigatórios."));

        var existing = await _channelRepo.FindByPhoneNumberIdAsync(phoneNumberId.Trim(), cancellationToken);
        if (existing is not null)
            return Result.Failure<WhatsAppChannelConfig>(Error.Conflict("WhatsApp.DuplicatePhoneNumberId", $"PhoneNumberId '{phoneNumberId}' já configurado."));

        var res = WhatsAppChannelConfig.Create(
            guard.Value,
            storeTradeName,
            displayPhoneNumber,
            phoneNumberId,
            businessAccountId,
            _secretProtector.Protect(rawAccessToken.Trim()),
            _secretProtector.Protect(rawAppSecret.Trim()),
            _secretProtector.HashVerifyToken(rawVerifyToken.Trim()),
            autoReplyMode,
            menuUrl,
            storeUnitId);

        if (res.IsFailure)
            return res;

        await _channelRepo.AddAsync(res.Value, cancellationToken);
        await _channelRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<TraditionalMenuBotRule>> AddBotRuleAsync(
        User adminActor,
        Guid channelConfigId,
        string ruleName,
        string triggerKeyword,
        BotRuleMatchMode matchMode,
        string replyTemplate,
        bool includeMenuLink = false,
        bool escalateToHuman = false,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(adminActor, PermissionAction.Approve);
        if (guard.IsFailure)
            return Result.Failure<TraditionalMenuBotRule>(guard.Error);

        var channel = await _channelRepo.FindByIdAsync(channelConfigId, cancellationToken);
        if (channel is null)
            return Result.Failure<TraditionalMenuBotRule>(Error.NotFound("WhatsApp.ChannelNotFound", "Canal WhatsApp não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, channel);

        var res = TraditionalMenuBotRule.Create(
            guard.Value,
            channel.Id,
            ruleName,
            triggerKeyword,
            matchMode,
            replyTemplate,
            includeMenuLink,
            escalateToHuman,
            displayOrder);

        if (res.IsFailure)
            return res;

        await _botRuleRepo.AddAsync(res.Value, cancellationToken);
        await _botRuleRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<WhatsAppConversation>> ReceiveInboundWebhookAsync(
        string phoneNumberId,
        string externalMessageId,
        string customerPhone,
        string? customerName,
        string messageText,
        string? rawWebhookBody = null,
        string? xHubSignature256 = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(phoneNumberId))
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.PhoneIdRequired", "PhoneNumberId é obrigatório."));
        if (string.IsNullOrWhiteSpace(externalMessageId))
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.ExternalMsgRequired", "Id da mensagem é obrigatório."));

        var channel = await _channelRepo.FindByPhoneNumberIdAsync(phoneNumberId.Trim(), cancellationToken);
        if (channel is null || !channel.IsActive)
            return Result.Failure<WhatsAppConversation>(Error.NotFound("WhatsApp.ChannelNotFound", "Canal WhatsApp ativo não encontrado para este estabelecimento."));

        TenantGuard.EnsureOwnership(_tenantContext, channel);

        if (rawWebhookBody is not null || xHubSignature256 is not null)
        {
            var appSecret = _secretProtector.Unprotect(channel.EncryptedAppSecretBlob);
            if (!_cloudGateway.VerifyPayloadSignature(rawWebhookBody ?? string.Empty, xHubSignature256, appSecret))
                return Result.Failure<WhatsAppConversation>(Error.Unauthorized("WhatsApp.InvalidWebhookSignature", "Assinatura X-Hub-Signature-256 inválida."));
        }

        var normalizedPhone = new string(customerPhone.Where(char.IsDigit).ToArray());
        var existingConv = await _convRepo.FindByCustomerPhoneAsync(channel.Id, normalizedPhone, cancellationToken);

        // Idempotência explícita de webhook
        if (await _convRepo.ExistsExternalMessageIdAsync(externalMessageId.Trim(), cancellationToken) && existingConv is not null)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existingConv);
            return Result.Success(existingConv);
        }

        var utcNow = _clock.UtcNow;
        channel.RecordWebhookActivity(utcNow);

        WhatsAppConversation conv;
        if (existingConv is null)
        {
            var startRes = WhatsAppConversation.Start(tenantId, channel.Id, customerPhone, customerName, utcNow);
            if (startRes.IsFailure)
                return startRes;

            conv = startRes.Value;
            await _convRepo.AddAsync(conv, cancellationToken);
        }
        else
        {
            TenantGuard.EnsureOwnership(_tenantContext, existingConv);
            conv = existingConv;
        }

        var recInRes = conv.RecordInbound(externalMessageId, messageText, utcNow);
        if (recInRes.IsFailure)
            return Result.Failure<WhatsAppConversation>(recInRes.Error);

        if (conv.Status is not (WhatsAppConversationStatus.WaitingHuman or WhatsAppConversationStatus.AssignedHuman) &&
            channel.AutoReplyMode != WhatsAppAutoReplyMode.HumanOnly)
        {
            await TryAutoRespondAsync(channel, conv, messageText, utcNow, cancellationToken);
        }

        await _convRepo.SaveChangesAsync(cancellationToken);

        await _inboxNotifier.NotifyAsync(new InboxRealtimeNotification(
            conv.TenantId,
            conv.Id,
            conv.CustomerPhone,
            conv.Status,
            conv.LastMessagePreview,
            utcNow), cancellationToken);

        return Result.Success(conv);
    }

    public async Task<Result<WhatsAppConversation>> SendHumanReplyAsync(
        User staffActor,
        Guid conversationId,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(staffActor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<WhatsAppConversation>(guard.Error);

        var conv = await _convRepo.FindByIdAsync(conversationId, cancellationToken);
        if (conv is null)
            return Result.Failure<WhatsAppConversation>(Error.NotFound("WhatsApp.ConversationNotFound", "Conversa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, conv);

        var channel = await _channelRepo.FindByIdAsync(conv.ChannelConfigId, cancellationToken);
        if (channel is null)
            return Result.Failure<WhatsAppConversation>(Error.NotFound("WhatsApp.ChannelNotFound", "Canal da conversa não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, channel);

        var utcNow = _clock.UtcNow;
        var sendRes = await _cloudGateway.SendTextMessageAsync(channel, conv.CustomerPhone, messageText.Trim(), cancellationToken);
        var recordRes = conv.RecordOutbound(
            WhatsAppMessageDirection.OutboundHumanAgent,
            staffActor.Id,
            messageText,
            deliveredToCloud: sendRes.IsSuccess,
            deliveryError: sendRes.IsFailure ? sendRes.Error.Message : null,
            groundedByFacts: true,
            utcNow);

        if (recordRes.IsFailure)
            return Result.Failure<WhatsAppConversation>(recordRes.Error);

        await _convRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(conv);
    }

    public async Task<Result<WhatsAppConversation>> ResolveConversationAsync(
        User staffActor,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(staffActor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<WhatsAppConversation>(guard.Error);

        var conv = await _convRepo.FindByIdAsync(conversationId, cancellationToken);
        if (conv is null)
            return Result.Failure<WhatsAppConversation>(Error.NotFound("WhatsApp.ConversationNotFound", "Conversa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, conv);
        conv.Resolve(_clock.UtcNow);
        await _convRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(conv);
    }

    public async Task<Result<IReadOnlyList<WhatsAppConversation>>> ListInboxAsync(
        User staffActor,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateActor(staffActor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<IReadOnlyList<WhatsAppConversation>>(guard.Error);

        var list = await _convRepo.ListInboxAsync(cancellationToken);
        return Result.Success(list);
    }

    private async Task TryAutoRespondAsync(
        WhatsAppChannelConfig channel,
        WhatsAppConversation conv,
        string incomingText,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // 1. Tenta Bot Tradicional (se TraditionalBotOnly ou HybridBotThenAi)
        if (channel.AutoReplyMode is WhatsAppAutoReplyMode.TraditionalBotOnly or WhatsAppAutoReplyMode.HybridBotThenAi)
        {
            var rules = await _botRuleRepo.ListOrderedForChannelAsync(channel.Id, cancellationToken);
            var matched = rules.FirstOrDefault(r => r.Matches(incomingText));
            if (matched is not null)
            {
                var reply = matched.IncludeMenuLink && !string.IsNullOrWhiteSpace(channel.MenuUrl)
                    ? $"{matched.ReplyTemplate}\nCardápio: {channel.MenuUrl}"
                    : matched.ReplyTemplate;

                var sendRes = await _cloudGateway.SendTextMessageAsync(channel, conv.CustomerPhone, reply, cancellationToken);
                conv.RecordOutbound(
                    WhatsAppMessageDirection.OutboundTraditionalBot,
                    null,
                    reply,
                    sendRes.IsSuccess,
                    sendRes.IsFailure ? sendRes.Error.Message : null,
                    groundedByFacts: true,
                    utcNow);

                if (matched.EscalateToHuman)
                    conv.RequestHumanHandoff($"Regra '{matched.RuleName}' encaminhou para atendente.", utcNow);

                return;
            }
        }

        // 2. Se modo inclui IA restrita ao tenant (AiGroundedAssistant ou HybridBotThenAi sem regra tradicional)
        if (channel.AutoReplyMode is WhatsAppAutoReplyMode.AiGroundedAssistant or WhatsAppAutoReplyMode.HybridBotThenAi)
        {
            var snapshot = await _knowledgeProvider.GetSnapshotForCustomerAsync(channel.TenantId, conv.CustomerPhone, cancellationToken);
            if (snapshot is null || snapshot.TenantId != channel.TenantId)
            {
                conv.RequestHumanHandoff("Base de conhecimento verificada indisponível para resposta automática.", utcNow);
                return;
            }

            var aiRes = await _aiGateway.GenerateGroundedReplyAsync(snapshot, incomingText, cancellationToken);
            if (aiRes.IsFailure)
            {
                conv.RequestHumanHandoff($"IA indisponível: {aiRes.Error.Message}", utcNow);
                return;
            }

            var validation = AiGroundingValidator.ValidateResponse(snapshot, aiRes.Value);
            if (validation.IsFailure)
            {
                conv.RequestHumanHandoff($"Bloqueio anti-alucinação ({validation.Error.Code}): {validation.Error.Message}", utcNow);
                return;
            }

            var sendAi = await _cloudGateway.SendTextMessageAsync(channel, conv.CustomerPhone, aiRes.Value, cancellationToken);
            conv.RecordOutbound(
                WhatsAppMessageDirection.OutboundAiGrounded,
                null,
                aiRes.Value,
                sendAi.IsSuccess,
                sendAi.IsFailure ? sendAi.Error.Message : null,
                groundedByFacts: true,
                utcNow);
            return;
        }

        // 3. Fallback determinístico para modo tradicional sem regra casada -> transbordo humano ou menu da loja
        var menuNotice = !string.IsNullOrWhiteSpace(channel.MenuUrl)
            ? $"Olá! Bem-vindo(a) ao {channel.StoreTradeName}. Confira nosso cardápio em {channel.MenuUrl} ou aguarde um atendente."
            : $"Olá! Bem-vindo(a) ao {channel.StoreTradeName}. Um atendente responderá em instantes.";

        var sendFallback = await _cloudGateway.SendTextMessageAsync(channel, conv.CustomerPhone, menuNotice, cancellationToken);
        conv.RecordOutbound(
            WhatsAppMessageDirection.OutboundTraditionalBot,
            null,
            menuNotice,
            sendFallback.IsSuccess,
            sendFallback.IsFailure ? sendFallback.Error.Message : null,
            groundedByFacts: true,
            utcNow);
        conv.RequestHumanHandoff("Mensagem não correspondida por regra automática.", utcNow);
    }

    private Result<Guid> ValidateActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != tenantId)
            throw new CrossTenantAccessException(actor.TenantId, tenantId, "Tentativa de acessar atendimento WhatsApp de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.WhatsApp, action))
            return Result.Failure<Guid>(Error.Forbidden("WhatsApp.Forbidden", "Permissão insuficiente para operar módulo WhatsApp."));

        return Result.Success(tenantId);
    }
}
