using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.WhatsAppSupport.Application.Abstractions;

public interface IWhatsAppChannelConfigRepository
{
    Task<WhatsAppChannelConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WhatsAppChannelConfig?> FindByPhoneNumberIdAsync(string phoneNumberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppChannelConfig>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(WhatsAppChannelConfig config, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITraditionalMenuBotRuleRepository
{
    Task<IReadOnlyList<TraditionalMenuBotRule>> ListOrderedForChannelAsync(Guid channelConfigId, CancellationToken cancellationToken = default);
    Task AddAsync(TraditionalMenuBotRule rule, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWhatsAppConversationRepository
{
    Task<WhatsAppConversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WhatsAppConversation?> FindByCustomerPhoneAsync(Guid channelConfigId, string customerPhone, CancellationToken cancellationToken = default);
    Task<bool> ExistsExternalMessageIdAsync(string externalMessageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppConversation>> ListInboxAsync(CancellationToken cancellationToken = default);
    Task AddAsync(WhatsAppConversation conversation, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWhatsAppSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
    string HashVerifyToken(string rawToken);
}

/// <summary>
/// Gateway oficial da Meta WhatsApp Business Cloud API (assinatura X-Hub-Signature-256 e envio de mensagens).
/// </summary>
public interface IWhatsAppCloudGateway
{
    bool VerifyPayloadSignature(string rawBody, string? xHubSignature256, string decryptedAppSecret);
    Task<Result<string>> SendTextMessageAsync(WhatsAppChannelConfig channel, string toPhone, string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provedor estritamente isolado por tenant com dados verificados de cardápio, preços, cupons e pedidos do cliente.
/// </summary>
public interface ITenantKnowledgeProvider
{
    Task<GroundedTenantKnowledgeSnapshot?> GetSnapshotForCustomerAsync(Guid tenantId, string customerPhone, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gateway para modelo de IA generativa respondendo exclusivamente a partir do snapshot verificado do tenant.
/// </summary>
public interface IAiAssistantGateway
{
    Task<Result<string>> GenerateGroundedReplyAsync(
        GroundedTenantKnowledgeSnapshot snapshot,
        string customerMessage,
        CancellationToken cancellationToken = default);
}

public sealed record InboxRealtimeNotification(
    Guid TenantId,
    Guid ConversationId,
    string CustomerPhone,
    WhatsAppConversationStatus Status,
    string LastPreview,
    DateTimeOffset OccurredAtUtc);

public interface IInboxRealtimeNotifier
{
    Task NotifyAsync(InboxRealtimeNotification notification, CancellationToken cancellationToken = default);
}
