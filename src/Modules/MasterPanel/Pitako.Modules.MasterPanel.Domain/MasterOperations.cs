using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.MasterPanel.Domain;

public enum SupportTicketStatus
{
    Open = 1,
    InProgress = 2,
    WaitingCustomer = 3,
    Resolved = 4,
    Closed = 5
}

public enum SupportTicketPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Chamado de suporte gerenciado pelo Painel Master (Seção 8).
/// </summary>
public sealed class SupportTicket : AggregateRoot<Guid>, IAuditableEntity
{
    private SupportTicket()
    {
        Subject = string.Empty;
        Description = string.Empty;
    }

    private SupportTicket(
        Guid id,
        Guid customerAccountId,
        Guid? tenantId,
        string subject,
        string description,
        SupportTicketPriority priority) : base(id)
    {
        CustomerAccountId = customerAccountId;
        TenantId = tenantId;
        Subject = subject;
        Description = description;
        Priority = priority;
        Status = SupportTicketStatus.Open;
    }

    public Guid CustomerAccountId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string Subject { get; private set; }
    public string Description { get; private set; }
    public SupportTicketPriority Priority { get; private set; }
    public SupportTicketStatus Status { get; private set; }
    public Guid? AssignedToSaasUserId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static Result<SupportTicket> Open(
        Guid customerAccountId,
        Guid? tenantId,
        string subject,
        string description,
        SupportTicketPriority priority = SupportTicketPriority.Normal)
    {
        if (customerAccountId == Guid.Empty)
            return Result.Failure<SupportTicket>(Error.Validation("Support.CustomerRequired", "Cliente SaaS é obrigatório."));

        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure<SupportTicket>(Error.Validation("Support.SubjectRequired", "Assunto é obrigatório."));

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<SupportTicket>(Error.Validation("Support.DescriptionRequired", "Descrição é obrigatória."));

        return Result.Success(new SupportTicket(
            Guid.NewGuid(),
            customerAccountId,
            tenantId,
            subject.Trim(),
            description.Trim(),
            priority));
    }

    public void AssignTo(Guid saasUserId)
    {
        AssignedToSaasUserId = saasUserId;
        if (Status == SupportTicketStatus.Open)
            Status = SupportTicketStatus.InProgress;
    }

    public void Resolve(string notes, DateTimeOffset utcNow)
    {
        Status = SupportTicketStatus.Resolved;
        ResolutionNotes = notes?.Trim();
        ResolvedAtUtc = utcNow;
    }

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}

/// <summary>
/// Configuração global da plataforma PITAKO administrada exclusivamente pelo Painel Master.
/// </summary>
public sealed class GlobalPlatformSetting : Entity<Guid>, IAuditableEntity
{
    private GlobalPlatformSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public GlobalPlatformSetting(Guid id, string key, string value, string? description = null, bool isSecret = false)
        : base(id)
    {
        Key = key.Trim().ToLowerInvariant();
        Value = value;
        Description = description?.Trim();
        IsSecret = isSecret;
    }

    public string Key { get; private set; }
    public string Value { get; private set; }
    public string? Description { get; private set; }
    public bool IsSecret { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public void UpdateValue(string newValue, string? description = null)
    {
        Value = newValue;
        if (description is not null)
            Description = description.Trim();
    }

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}
