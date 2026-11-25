using Pitako.SharedKernel.Abstractions;

namespace Pitako.Modules.Security.Domain;

/// <summary>
/// Eventos auditáveis críticos de segurança, autenticação, sessão, recuperação de senha e permissões.
/// </summary>
public enum SecurityEventType
{
    UserCreated = 1,
    LoginSucceeded = 2,
    LoginFailed = 3,
    LoginLockedOut = 4,
    LoginRateLimited = 5,
    SessionRevoked = 6,
    AllSessionsRevoked = 7,
    PasswordResetRequested = 8,
    PasswordResetCompleted = 9,
    PasswordResetRejected = 10,
    PermissionsUpdated = 11,
    CrossTenantAttemptBlocked = 12,
    UnauthorizedMasterAccessAttempt = 13
}

/// <summary>
/// Registro imutável de auditoria de segurança do sistema.
/// </summary>
public sealed class SecurityAuditLog : Entity<Guid>
{
    private SecurityAuditLog()
    {
        ActionDescription = string.Empty;
    }

    public SecurityAuditLog(
        Guid id,
        SecurityEventType eventType,
        UserScope? scope,
        Guid? tenantId,
        Guid? actorUserId,
        Guid? targetUserId,
        string? email,
        string? ipAddress,
        bool succeeded,
        string actionDescription,
        DateTimeOffset occurredAtUtc) : base(id)
    {
        EventType = eventType;
        Scope = scope;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        Email = email;
        IpAddress = ipAddress;
        Succeeded = succeeded;
        ActionDescription = actionDescription;
        OccurredAtUtc = occurredAtUtc;
    }

    public SecurityEventType EventType { get; }
    public UserScope? Scope { get; }
    public Guid? TenantId { get; }
    public Guid? ActorUserId { get; }
    public Guid? TargetUserId { get; }
    public string? Email { get; }
    public string? IpAddress { get; }
    public bool Succeeded { get; }
    public string ActionDescription { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}
