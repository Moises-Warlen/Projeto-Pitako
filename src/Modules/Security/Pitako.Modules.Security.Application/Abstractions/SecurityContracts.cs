using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Security.Application.Abstractions;

/// <summary>
/// Políticas configuráveis de segurança para autenticação, lockout e recuperação de senha.
/// </summary>
public sealed class SecurityPolicyOptions
{
    public int MaxFailedLoginAttempts { get; init; } = 5;
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan SessionDuration { get; init; } = TimeSpan.FromHours(12);
    public TimeSpan PasswordResetTokenLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public int RateLimitMaxAttemptsPerWindow { get; init; } = 10;
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(5);
}

public interface IPasswordHasher
{
    string HashPassword(string plainPassword);
    bool VerifyPassword(string plainPassword, string passwordHash);
}

public interface IPasswordStrengthValidator
{
    Result Validate(string plainPassword);
}

public interface ITokenGenerator
{
    string GenerateUrlSafeToken(int byteLength = 32);
    string ComputeSha256Hash(string rawToken);
}

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record AccessTokenClaims(
    Guid UserId,
    Guid SessionId,
    UserScope Scope,
    Guid? TenantId,
    string SecurityStamp);

public interface IAccessTokenService
{
    IssuedAccessToken IssueAccessToken(User user, Guid sessionId);
    AccessTokenClaims? ValidateAccessToken(string token);
}

public interface ILoginRateLimiter
{
    bool IsAllowed(string key, DateTimeOffset utcNow);
    void RegisterAttempt(string key, DateTimeOffset utcNow);
    void Reset(string key);
}

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(UserScope scope, Guid? tenantId, string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<User?> FindByPasswordResetTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<User?> FindBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISecurityAuditRepository
{
    Task AddAsync(SecurityAuditLog auditLog, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecurityAuditLog>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SecurityAuditLog>> ListSaasAsync(CancellationToken cancellationToken = default);
}
