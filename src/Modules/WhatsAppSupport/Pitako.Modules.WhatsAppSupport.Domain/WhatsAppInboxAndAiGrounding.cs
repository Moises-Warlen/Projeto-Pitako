using System.Globalization;
using System.Text.RegularExpressions;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.WhatsAppSupport.Domain;

public enum WhatsAppConversationStatus
{
    OpenBot = 1,
    WaitingHuman = 2,
    AssignedHuman = 3,
    Resolved = 4
}

public enum WhatsAppMessageDirection
{
    Inbound = 1,
    OutboundTraditionalBot = 2,
    OutboundAiGrounded = 3,
    OutboundHumanAgent = 4
}

public enum WhatsAppDeliveryStatus
{
    Received = 1,
    SentToCloud = 2,
    DeliveryFailed = 3
}

/// <summary>
/// Mensagem individual persistida na caixa de entrada WhatsApp do tenant (Seção 29).
/// </summary>
public sealed class WhatsAppMessage : Entity<Guid>, ITenantScopedEntity
{
    private WhatsAppMessage()
    {
        ContentText = string.Empty;
    }

    internal WhatsAppMessage(
        Guid id,
        Guid tenantId,
        Guid conversationId,
        string? externalMessageId,
        WhatsAppMessageDirection direction,
        Guid? senderUserId,
        string contentText,
        WhatsAppDeliveryStatus deliveryStatus,
        bool groundedByFacts,
        string? deliveryError,
        DateTimeOffset sentAtUtc) : base(id)
    {
        TenantId = tenantId;
        ConversationId = conversationId;
        ExternalMessageId = externalMessageId;
        Direction = direction;
        SenderUserId = senderUserId;
        ContentText = contentText;
        DeliveryStatus = deliveryStatus;
        GroundedByFacts = groundedByFacts;
        DeliveryError = deliveryError;
        SentAtUtc = sentAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public WhatsAppMessageDirection Direction { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public string ContentText { get; private set; }
    public WhatsAppDeliveryStatus DeliveryStatus { get; private set; }
    public bool GroundedByFacts { get; private set; }
    public string? DeliveryError { get; private set; }
    public DateTimeOffset SentAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em WhatsAppMessage.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Conversa de atendimento WhatsApp com suporte a bot tradicional, IA restrita a fatos e transbordo humano (Seção 29).
/// </summary>
public sealed class WhatsAppConversation : TenantScopedAggregateRoot
{
    private readonly List<WhatsAppMessage> _messages = new();

    private WhatsAppConversation()
    {
        CustomerPhone = string.Empty;
        CustomerName = string.Empty;
        LastMessagePreview = string.Empty;
    }

    private WhatsAppConversation(
        Guid id,
        Guid tenantId,
        Guid channelConfigId,
        string customerPhone,
        string customerName,
        DateTimeOffset startedAtUtc) : base(id, tenantId)
    {
        ChannelConfigId = channelConfigId;
        CustomerPhone = customerPhone;
        CustomerName = customerName;
        Status = WhatsAppConversationStatus.OpenBot;
        LastMessagePreview = string.Empty;
        UnreadCount = 0;
        StartedAtUtc = startedAtUtc;
        LastInteractionAtUtc = startedAtUtc;
    }

    public Guid ChannelConfigId { get; private set; }
    public string CustomerPhone { get; private set; }
    public string CustomerName { get; private set; }
    public WhatsAppConversationStatus Status { get; private set; }
    public Guid? AssignedAgentUserId { get; private set; }
    public string? HumanHandoffReason { get; private set; }
    public string LastMessagePreview { get; private set; }
    public int UnreadCount { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset LastInteractionAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public IReadOnlyCollection<WhatsAppMessage> Messages => _messages.AsReadOnly();

    public static Result<WhatsAppConversation> Start(
        Guid tenantId,
        Guid channelConfigId,
        string customerPhone,
        string? customerName,
        DateTimeOffset startedAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.TenantRequired", "TenantId é obrigatório."));
        if (channelConfigId == Guid.Empty)
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.ChannelRequired", "Canal WhatsApp é obrigatório."));
        if (string.IsNullOrWhiteSpace(customerPhone))
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.CustomerPhoneRequired", "Telefone do cliente é obrigatório."));

        var normalizedPhone = new string(customerPhone.Where(char.IsDigit).ToArray());
        if (normalizedPhone.Length < 8)
            return Result.Failure<WhatsAppConversation>(Error.Validation("WhatsApp.InvalidPhone", "Telefone WhatsApp inválido."));

        return Result.Success(new WhatsAppConversation(
            Guid.NewGuid(),
            tenantId,
            channelConfigId,
            normalizedPhone,
            string.IsNullOrWhiteSpace(customerName) ? normalizedPhone : customerName.Trim(),
            startedAtUtc));
    }

    public Result<WhatsAppMessage> RecordInbound(string externalMessageId, string text, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(externalMessageId))
            return Result.Failure<WhatsAppMessage>(Error.Validation("WhatsApp.ExternalMsgRequired", "Id externo da mensagem é obrigatório."));
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure<WhatsAppMessage>(Error.Validation("WhatsApp.MessageEmpty", "Conteúdo da mensagem não pode ser vazio."));

        var existing = _messages.FirstOrDefault(m => string.Equals(m.ExternalMessageId, externalMessageId.Trim(), StringComparison.Ordinal));
        if (existing is not null)
            return Result.Success(existing);

        if (Status == WhatsAppConversationStatus.Resolved)
            Status = WhatsAppConversationStatus.OpenBot;

        var trimmed = text.Trim();
        var msg = new WhatsAppMessage(
            Guid.NewGuid(),
            TenantId,
            Id,
            externalMessageId.Trim(),
            WhatsAppMessageDirection.Inbound,
            null,
            trimmed,
            WhatsAppDeliveryStatus.Received,
            groundedByFacts: true,
            deliveryError: null,
            utcNow);

        _messages.Add(msg);
        UnreadCount++;
        LastInteractionAtUtc = utcNow;
        LastMessagePreview = trimmed.Length > 120 ? trimmed[..120] : trimmed;
        return Result.Success(msg);
    }

    public Result<WhatsAppMessage> RecordOutbound(
        WhatsAppMessageDirection direction,
        Guid? senderUserId,
        string contentText,
        bool deliveredToCloud,
        string? deliveryError,
        bool groundedByFacts,
        DateTimeOffset utcNow)
    {
        if (direction == WhatsAppMessageDirection.Inbound)
            return Result.Failure<WhatsAppMessage>(Error.Validation("WhatsApp.InvalidDirection", "Direção deve ser de saída."));
        if (string.IsNullOrWhiteSpace(contentText))
            return Result.Failure<WhatsAppMessage>(Error.Validation("WhatsApp.MessageEmpty", "Texto de resposta não pode ser vazio."));

        var trimmed = contentText.Trim();
        var msg = new WhatsAppMessage(
            Guid.NewGuid(),
            TenantId,
            Id,
            null,
            direction,
            senderUserId,
            trimmed,
            deliveredToCloud ? WhatsAppDeliveryStatus.SentToCloud : WhatsAppDeliveryStatus.DeliveryFailed,
            groundedByFacts,
            deliveredToCloud ? null : deliveryError,
            utcNow);

        _messages.Add(msg);
        LastInteractionAtUtc = utcNow;
        LastMessagePreview = trimmed.Length > 120 ? trimmed[..120] : trimmed;

        if (direction == WhatsAppMessageDirection.OutboundHumanAgent && senderUserId.HasValue)
        {
            AssignedAgentUserId = senderUserId;
            Status = WhatsAppConversationStatus.AssignedHuman;
            UnreadCount = 0;
        }

        return Result.Success(msg);
    }

    public void RequestHumanHandoff(string reason, DateTimeOffset utcNow)
    {
        Status = WhatsAppConversationStatus.WaitingHuman;
        HumanHandoffReason = string.IsNullOrWhiteSpace(reason) ? "Solicitado atendimento humano." : reason.Trim();
        LastInteractionAtUtc = utcNow;
    }

    public void AssignAgent(Guid agentUserId, DateTimeOffset utcNow)
    {
        if (agentUserId == Guid.Empty)
            throw new ArgumentException("Agente obrigatório.", nameof(agentUserId));

        AssignedAgentUserId = agentUserId;
        Status = WhatsAppConversationStatus.AssignedHuman;
        UnreadCount = 0;
        LastInteractionAtUtc = utcNow;
    }

    public void Resolve(DateTimeOffset utcNow)
    {
        Status = WhatsAppConversationStatus.Resolved;
        ResolvedAtUtc = utcNow;
        UnreadCount = 0;
    }
}

public sealed record GroundedMenuItem(string ProductName, decimal UnitPriceAmount, bool IsAvailable, string? CategoryName = null);

public sealed record GroundedOrderFact(string OrderNumber, string CurrentStatus, decimal TotalAmount);

/// <summary>
/// Fotografia estritamente isolada por tenant com dados reais verificados (cardápio, preços, cupons e pedido)
/// usada para restringir respostas de IA sem alucinação nem vazamento cross-tenant (Seção 29).
/// </summary>
public sealed record GroundedTenantKnowledgeSnapshot(
    Guid TenantId,
    string StoreTradeName,
    string? OpeningHoursSummary,
    string? MenuUrl,
    IReadOnlyList<GroundedMenuItem> MenuItems,
    IReadOnlyList<string> ActiveCouponCodes,
    GroundedOrderFact? CustomerLastOrder);

/// <summary>
/// Validador anti-alucinação para respostas geradas por IA no WhatsApp: bloqueia valores monetários
/// ou números de pedido inexistentes na base real do estabelecimento e aciona transbordo humano seguro.
/// </summary>
public static partial class AiGroundingValidator
{
    [GeneratedRegex(@"R\$\s*(\d+(?:[.,]\d{2}))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"#(\d{3,6})", RegexOptions.Compiled)]
    private static partial Regex OrderNumberRegex();

    public static Result ValidateResponse(GroundedTenantKnowledgeSnapshot snapshot, string aiReplyText)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TenantId == Guid.Empty)
            return Result.Failure(Error.Validation("AiGrounding.TenantRequired", "Contexto de tenant obrigatório."));
        if (string.IsNullOrWhiteSpace(aiReplyText))
            return Result.Failure(Error.Validation("AiGrounding.EmptyReply", "Resposta vazia da IA."));

        var allowedPrices = new HashSet<decimal>(snapshot.MenuItems.Select(i => decimal.Round(i.UnitPriceAmount, 2)));
        if (snapshot.CustomerLastOrder is not null)
            allowedPrices.Add(decimal.Round(snapshot.CustomerLastOrder.TotalAmount, 2));

        foreach (Match match in PriceRegex().Matches(aiReplyText))
        {
            var rawVal = match.Groups[1].Value.Replace(',', '.');
            if (decimal.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var mentionedPrice))
            {
                if (!allowedPrices.Contains(decimal.Round(mentionedPrice, 2)))
                {
                    return Result.Failure(Error.Conflict(
                        "AiGrounding.UnverifiedPriceClaim",
                        $"Resposta da IA citou valor R$ {mentionedPrice:N2} não confirmado nos dados reais do estabelecimento."));
                }
            }
        }

        foreach (Match match in OrderNumberRegex().Matches(aiReplyText))
        {
            var mentionedOrder = $"#{match.Groups[1].Value}";
            if (snapshot.CustomerLastOrder is null ||
                !string.Equals(snapshot.CustomerLastOrder.OrderNumber, mentionedOrder, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.Conflict(
                    "AiGrounding.UnverifiedOrderReference",
                    $"Resposta da IA citou pedido {mentionedOrder} não encontrado para este cliente/tenant."));
            }
        }

        return Result.Success();
    }
}
