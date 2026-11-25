using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.WhatsAppSupport.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do módulo WhatsApp Business, Bot Tradicional, Inbox e IA com isolamento multi-tenant.
/// </summary>
public sealed class WhatsAppDbContext : PitakoDbContextBase
{
    public const string SchemaName = "whatsapp";

    public WhatsAppDbContext(
        DbContextOptions<WhatsAppDbContext> options,
        ITenantContext tenantContext,
        IClock clock) : base(options, tenantContext, clock)
    {
    }

    public DbSet<WhatsAppChannelConfig> Channels => Set<WhatsAppChannelConfig>();
    public DbSet<TraditionalMenuBotRule> BotRules => Set<TraditionalMenuBotRule>();
    public DbSet<WhatsAppConversation> Conversations => Set<WhatsAppConversation>();
    public DbSet<WhatsAppMessage> Messages => Set<WhatsAppMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.ApplyConfiguration(new WhatsAppChannelConfigConfiguration());
        modelBuilder.ApplyConfiguration(new TraditionalMenuBotRuleConfiguration());
        modelBuilder.ApplyConfiguration(new WhatsAppConversationConfiguration());
        modelBuilder.ApplyConfiguration(new WhatsAppMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class WhatsAppChannelConfigConfiguration : IEntityTypeConfiguration<WhatsAppChannelConfig>
{
    public void Configure(EntityTypeBuilder<WhatsAppChannelConfig> builder)
    {
        builder.ToTable("channel_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreUnitId).HasColumnName("store_unit_id");
        builder.Property(x => x.StoreTradeName).HasColumnName("store_trade_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(40).IsRequired();
        builder.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.BusinessAccountId).HasColumnName("business_account_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.EncryptedAccessTokenBlob).HasColumnName("encrypted_access_token_blob").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.EncryptedAppSecretBlob).HasColumnName("encrypted_app_secret_blob").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.WebhookVerifyTokenHash).HasColumnName("webhook_verify_token_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.AutoReplyMode).HasColumnName("auto_reply_mode").IsRequired();
        builder.Property(x => x.MenuUrl).HasColumnName("menu_url").HasMaxLength(300);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.LastWebhookUtc).HasColumnName("last_webhook_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.PhoneNumberId }).IsUnique();
    }
}

internal sealed class TraditionalMenuBotRuleConfiguration : IEntityTypeConfiguration<TraditionalMenuBotRule>
{
    public void Configure(EntityTypeBuilder<TraditionalMenuBotRule> builder)
    {
        builder.ToTable("bot_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ChannelConfigId).HasColumnName("channel_config_id").IsRequired();
        builder.Property(x => x.RuleName).HasColumnName("rule_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.TriggerKeyword).HasColumnName("trigger_keyword").HasMaxLength(120).IsRequired();
        builder.Property(x => x.MatchMode).HasColumnName("match_mode").IsRequired();
        builder.Property(x => x.ReplyTemplate).HasColumnName("reply_template").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.IncludeMenuLink).HasColumnName("include_menu_link").IsRequired();
        builder.Property(x => x.EscalateToHuman).HasColumnName("escalate_to_human").IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.TenantId, x.ChannelConfigId, x.DisplayOrder });
    }
}

internal sealed class WhatsAppConversationConfiguration : IEntityTypeConfiguration<WhatsAppConversation>
{
    public void Configure(EntityTypeBuilder<WhatsAppConversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ChannelConfigId).HasColumnName("channel_config_id").IsRequired();
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.AssignedAgentUserId).HasColumnName("assigned_agent_user_id");
        builder.Property(x => x.HumanHandoffReason).HasColumnName("human_handoff_reason").HasMaxLength(300);
        builder.Property(x => x.LastMessagePreview).HasColumnName("last_message_preview").HasMaxLength(160).IsRequired();
        builder.Property(x => x.UnreadCount).HasColumnName("unread_count").IsRequired();
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(x => x.LastInteractionAtUtc).HasColumnName("last_interaction_at_utc").IsRequired();
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.ChannelConfigId, x.CustomerPhone }).IsUnique();
    }
}

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(160);
        builder.Property(x => x.Direction).HasColumnName("direction").IsRequired();
        builder.Property(x => x.SenderUserId).HasColumnName("sender_user_id");
        builder.Property(x => x.ContentText).HasColumnName("content_text").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").IsRequired();
        builder.Property(x => x.GroundedByFacts).HasColumnName("grounded_by_facts").IsRequired();
        builder.Property(x => x.DeliveryError).HasColumnName("delivery_error").HasMaxLength(500);
        builder.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ExternalMessageId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ConversationId, x.SentAtUtc });
    }
}
