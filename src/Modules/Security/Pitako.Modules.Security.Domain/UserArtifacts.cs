using Pitako.SharedKernel.Abstractions;

namespace Pitako.Modules.Security.Domain;

/// <summary>
/// Permissão personalizada por módulo atribuída a um usuário, permitindo refinar o perfil padrão.
/// </summary>
public sealed class UserModulePermission : Entity<Guid>
{
    private UserModulePermission()
    {
        ModuleCode = string.Empty;
    }

    internal UserModulePermission(Guid id, Guid userId, string moduleCode, PermissionAction allowedActions)
        : base(id)
    {
        UserId = userId;
        ModuleCode = SystemModules.Normalize(moduleCode);
        AllowedActions = allowedActions;
    }

    public Guid UserId { get; private set; }
    public string ModuleCode { get; private set; }
    public PermissionAction AllowedActions { get; private set; }

    internal void UpdateActions(PermissionAction actions) => AllowedActions = actions;
}

/// <summary>
/// Sessão autenticada emitida para controle de sessões simultâneas, revogação e invalidação pós-troca de senha.
/// </summary>
public sealed class UserSession : Entity<Guid>
{
    private UserSession()
    {
        RefreshTokenHash = string.Empty;
    }

    internal UserSession(
        Guid id,
        Guid userId,
        Guid? tenantId,
        string refreshTokenHash,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        string? ipAddress,
        string? userAgent,
        string? deviceId) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        RefreshTokenHash = refreshTokenHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DeviceId = deviceId;
    }

    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string RefreshTokenHash { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? DeviceId { get; private set; }

    public bool IsActive(DateTimeOffset utcNow) =>
        RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    internal void Revoke(DateTimeOffset utcNow, string reason)
    {
        if (RevokedAtUtc is not null)
            return;

        RevokedAtUtc = utcNow;
        RevocationReason = string.IsNullOrWhiteSpace(reason) ? "Revogada" : reason.Trim();
    }
}

/// <summary>
/// Token seguro de uso único e validade limitada para recuperação de senha (Seção 9).
/// Armazena somente o hash criptográfico do token, nunca o valor bruto enviado no link.
/// </summary>
public sealed class PasswordResetToken : Entity<Guid>
{
    private PasswordResetToken()
    {
        TokenHash = string.Empty;
    }

    internal PasswordResetToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        string? requestedFromIp) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RequestedFromIp = requestedFromIp;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public DateTimeOffset? InvalidatedAtUtc { get; private set; }
    public string? RequestedFromIp { get; private set; }

    public bool CanBeUsed(DateTimeOffset utcNow) =>
        UsedAtUtc is null && InvalidatedAtUtc is null && ExpiresAtUtc > utcNow;

    internal void MarkUsed(DateTimeOffset utcNow) => UsedAtUtc = utcNow;

    internal void Invalidate(DateTimeOffset utcNow)
    {
        if (UsedAtUtc is null && InvalidatedAtUtc is null)
            InvalidatedAtUtc = utcNow;
    }
}
