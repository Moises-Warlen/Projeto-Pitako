using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.WhatsAppSupport.Domain;

public enum WhatsAppAutoReplyMode
{
    TraditionalBotOnly = 1,
    AiGroundedAssistant = 2,
    HybridBotThenAi = 3,
    HumanOnly = 4
}

public enum BotRuleMatchMode
{
    Exact = 1,
    Contains = 2
}

/// <summary>
/// Configuração oficial do canal WhatsApp Business Cloud API por estabelecimento/unidade (Seção 29).
/// </summary>
public sealed class WhatsAppChannelConfig : TenantScopedAggregateRoot
{
    private WhatsAppChannelConfig()
    {
        StoreTradeName = string.Empty;
        DisplayPhoneNumber = string.Empty;
        PhoneNumberId = string.Empty;
        BusinessAccountId = string.Empty;
        EncryptedAccessTokenBlob = string.Empty;
        EncryptedAppSecretBlob = string.Empty;
        WebhookVerifyTokenHash = string.Empty;
    }

    private WhatsAppChannelConfig(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string storeTradeName,
        string displayPhoneNumber,
        string phoneNumberId,
        string businessAccountId,
        string encryptedAccessTokenBlob,
        string encryptedAppSecretBlob,
        string webhookVerifyTokenHash,
        WhatsAppAutoReplyMode autoReplyMode,
        string? menuUrl) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        StoreTradeName = storeTradeName;
        DisplayPhoneNumber = displayPhoneNumber;
        PhoneNumberId = phoneNumberId;
        BusinessAccountId = businessAccountId;
        EncryptedAccessTokenBlob = encryptedAccessTokenBlob;
        EncryptedAppSecretBlob = encryptedAppSecretBlob;
        WebhookVerifyTokenHash = webhookVerifyTokenHash;
        AutoReplyMode = autoReplyMode;
        MenuUrl = menuUrl;
        IsActive = true;
    }

    public Guid? StoreUnitId { get; private set; }
    public string StoreTradeName { get; private set; }
    public string DisplayPhoneNumber { get; private set; }
    public string PhoneNumberId { get; private set; }
    public string BusinessAccountId { get; private set; }
    public string EncryptedAccessTokenBlob { get; private set; }
    public string EncryptedAppSecretBlob { get; private set; }
    public string WebhookVerifyTokenHash { get; private set; }
    public WhatsAppAutoReplyMode AutoReplyMode { get; private set; }
    public string? MenuUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastWebhookUtc { get; private set; }

    public static Result<WhatsAppChannelConfig> Create(
        Guid tenantId,
        string storeTradeName,
        string displayPhoneNumber,
        string phoneNumberId,
        string businessAccountId,
        string encryptedAccessTokenBlob,
        string encryptedAppSecretBlob,
        string webhookVerifyTokenHash,
        WhatsAppAutoReplyMode autoReplyMode = WhatsAppAutoReplyMode.HybridBotThenAi,
        string? menuUrl = null,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(storeTradeName))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.StoreNameRequired", "Nome do estabelecimento é obrigatório."));
        if (string.IsNullOrWhiteSpace(displayPhoneNumber) || string.IsNullOrWhiteSpace(phoneNumberId))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.PhoneIdRequired", "Número e PhoneNumberId da Cloud API são obrigatórios."));
        if (string.IsNullOrWhiteSpace(businessAccountId))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.WabaRequired", "BusinessAccountId (WABA) é obrigatório."));
        if (string.IsNullOrWhiteSpace(encryptedAccessTokenBlob) || string.IsNullOrWhiteSpace(encryptedAppSecretBlob))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.CredentialsRequired", "Credenciais cifradas do canal WhatsApp são obrigatórias."));
        if (string.IsNullOrWhiteSpace(webhookVerifyTokenHash))
            return Result.Failure<WhatsAppChannelConfig>(Error.Validation("WhatsApp.VerifyTokenRequired", "Hash do token de verificação é obrigatório."));

        return Result.Success(new WhatsAppChannelConfig(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            storeTradeName.Trim(),
            displayPhoneNumber.Trim(),
            phoneNumberId.Trim(),
            businessAccountId.Trim(),
            encryptedAccessTokenBlob.Trim(),
            encryptedAppSecretBlob.Trim(),
            webhookVerifyTokenHash.Trim(),
            autoReplyMode,
            string.IsNullOrWhiteSpace(menuUrl) ? null : menuUrl.Trim()));
    }

    public void SetAutoReplyMode(WhatsAppAutoReplyMode mode) => AutoReplyMode = mode;
    public void RecordWebhookActivity(DateTimeOffset utcNow) => LastWebhookUtc = utcNow;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

/// <summary>
/// Regra determinística do bot tradicional de menu/opções por estabelecimento (Seção 29).
/// </summary>
public sealed class TraditionalMenuBotRule : TenantScopedAggregateRoot
{
    private TraditionalMenuBotRule()
    {
        RuleName = string.Empty;
        TriggerKeyword = string.Empty;
        ReplyTemplate = string.Empty;
    }

    private TraditionalMenuBotRule(
        Guid id,
        Guid tenantId,
        Guid channelConfigId,
        string ruleName,
        string triggerKeyword,
        BotRuleMatchMode matchMode,
        string replyTemplate,
        bool includeMenuLink,
        bool escalateToHuman,
        int displayOrder) : base(id, tenantId)
    {
        ChannelConfigId = channelConfigId;
        RuleName = ruleName;
        TriggerKeyword = triggerKeyword;
        MatchMode = matchMode;
        ReplyTemplate = replyTemplate;
        IncludeMenuLink = includeMenuLink;
        EscalateToHuman = escalateToHuman;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public Guid ChannelConfigId { get; private set; }
    public string RuleName { get; private set; }
    public string TriggerKeyword { get; private set; }
    public BotRuleMatchMode MatchMode { get; private set; }
    public string ReplyTemplate { get; private set; }
    public bool IncludeMenuLink { get; private set; }
    public bool EscalateToHuman { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<TraditionalMenuBotRule> Create(
        Guid tenantId,
        Guid channelConfigId,
        string ruleName,
        string triggerKeyword,
        BotRuleMatchMode matchMode,
        string replyTemplate,
        bool includeMenuLink = false,
        bool escalateToHuman = false,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TraditionalMenuBotRule>(Error.Validation("BotRule.TenantRequired", "TenantId é obrigatório."));
        if (channelConfigId == Guid.Empty)
            return Result.Failure<TraditionalMenuBotRule>(Error.Validation("BotRule.ChannelRequired", "Canal WhatsApp é obrigatório."));
        if (string.IsNullOrWhiteSpace(ruleName))
            return Result.Failure<TraditionalMenuBotRule>(Error.Validation("BotRule.NameRequired", "Nome da regra é obrigatório."));
        if (string.IsNullOrWhiteSpace(triggerKeyword))
            return Result.Failure<TraditionalMenuBotRule>(Error.Validation("BotRule.TriggerRequired", "Palavra-chave ou opção é obrigatória."));
        if (string.IsNullOrWhiteSpace(replyTemplate))
            return Result.Failure<TraditionalMenuBotRule>(Error.Validation("BotRule.ReplyRequired", "Texto de resposta é obrigatório."));

        return Result.Success(new TraditionalMenuBotRule(
            Guid.NewGuid(),
            tenantId,
            channelConfigId,
            ruleName.Trim(),
            triggerKeyword.Trim().ToLowerInvariant(),
            matchMode,
            replyTemplate.Trim(),
            includeMenuLink,
            escalateToHuman,
            displayOrder));
    }

    public bool Matches(string incomingText)
    {
        if (!IsActive || string.IsNullOrWhiteSpace(incomingText))
            return false;

        var normalized = incomingText.Trim().ToLowerInvariant();
        return MatchMode switch
        {
            BotRuleMatchMode.Exact => string.Equals(normalized, TriggerKeyword, StringComparison.Ordinal),
            BotRuleMatchMode.Contains => normalized.Contains(TriggerKeyword, StringComparison.Ordinal),
            _ => false
        };
    }

    public void Deactivate() => IsActive = false;
}
