using Microsoft.EntityFrameworkCore;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;

namespace Pitako.Modules.WhatsAppSupport.Infrastructure.Persistence;

public sealed class EfWhatsAppChannelConfigRepository(WhatsAppDbContext db) : IWhatsAppChannelConfigRepository
{
    public Task<WhatsAppChannelConfig?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Channels.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<WhatsAppChannelConfig?> FindByPhoneNumberIdAsync(string phoneNumberId, CancellationToken cancellationToken = default) =>
        db.Channels.FirstOrDefaultAsync(c => c.PhoneNumberId == phoneNumberId, cancellationToken);

    public async Task<IReadOnlyList<WhatsAppChannelConfig>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Channels.AsNoTracking().OrderBy(c => c.DisplayPhoneNumber).ToListAsync(cancellationToken);

    public async Task AddAsync(WhatsAppChannelConfig config, CancellationToken cancellationToken = default) =>
        await db.Channels.AddAsync(config, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTraditionalMenuBotRuleRepository(WhatsAppDbContext db) : ITraditionalMenuBotRuleRepository
{
    public async Task<IReadOnlyList<TraditionalMenuBotRule>> ListOrderedForChannelAsync(
        Guid channelConfigId,
        CancellationToken cancellationToken = default) =>
        await db.BotRules
            .Where(r => r.IsActive && r.ChannelConfigId == channelConfigId)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(TraditionalMenuBotRule rule, CancellationToken cancellationToken = default) =>
        await db.BotRules.AddAsync(rule, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfWhatsAppConversationRepository(WhatsAppDbContext db) : IWhatsAppConversationRepository
{
    public Task<WhatsAppConversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Conversations.Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<WhatsAppConversation?> FindByCustomerPhoneAsync(
        Guid channelConfigId,
        string customerPhone,
        CancellationToken cancellationToken = default) =>
        db.Conversations.Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.ChannelConfigId == channelConfigId && c.CustomerPhone == customerPhone, cancellationToken);

    public Task<bool> ExistsExternalMessageIdAsync(string externalMessageId, CancellationToken cancellationToken = default) =>
        db.Messages.AnyAsync(m => m.ExternalMessageId == externalMessageId, cancellationToken);

    public async Task<IReadOnlyList<WhatsAppConversation>> ListInboxAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.Conversations
                .Include(c => c.Messages)
                .AsNoTracking()
                .OrderByDescending(c => c.LastInteractionAtUtc)
                .ToListAsync(cancellationToken);
        }

        var list = await db.Conversations.Include(c => c.Messages).AsNoTracking().ToListAsync(cancellationToken);
        return list.OrderByDescending(c => c.LastInteractionAtUtc).ToList();
    }

    public async Task AddAsync(WhatsAppConversation conversation, CancellationToken cancellationToken = default) =>
        await db.Conversations.AddAsync(conversation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
