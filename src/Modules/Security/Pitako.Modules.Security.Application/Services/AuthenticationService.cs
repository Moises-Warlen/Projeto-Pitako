using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Security.Application.Services;

public sealed record LoginResult(
    Guid UserId,
    Guid SessionId,
    UserScope Scope,
    Guid? TenantId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset SessionExpiresAtUtc);

/// <summary>
/// Caso de uso de autenticação individual para contas SaaS e de estabelecimentos,
/// com proteção contra força bruta, rate limiting, gestão de sessões e auditoria completa.
/// </summary>
public sealed class AuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly ISecurityAuditRepository _auditRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IAccessTokenService _accessTokenService;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IClock _clock;
    private readonly SecurityPolicyOptions _policy;

    public AuthenticationService(
        IUserRepository userRepository,
        ISecurityAuditRepository auditRepository,
        IPasswordHasher passwordHasher,
        ITokenGenerator tokenGenerator,
        IAccessTokenService accessTokenService,
        ILoginRateLimiter rateLimiter,
        IClock clock,
        SecurityPolicyOptions? policy = null)
    {
        _userRepository = userRepository;
        _auditRepository = auditRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _accessTokenService = accessTokenService;
        _rateLimiter = rateLimiter;
        _clock = clock;
        _policy = policy ?? new SecurityPolicyOptions();
    }

    public Task<Result<LoginResult>> LoginSaasAsync(
        string email,
        string password,
        string? ipAddress = null,
        string? userAgent = null,
        string? deviceId = null,
        CancellationToken cancellationToken = default) =>
        LoginInternalAsync(UserScope.Saas, null, email, password, ipAddress, userAgent, deviceId, cancellationToken);

    public Task<Result<LoginResult>> LoginEstablishmentAsync(
        Guid tenantId,
        string email,
        string password,
        string? ipAddress = null,
        string? userAgent = null,
        string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return Task.FromResult(Result.Failure<LoginResult>(Error.Validation("Auth.TenantRequired", "Estabelecimento é obrigatório para login.")));

        return LoginInternalAsync(UserScope.Establishment, tenantId, email, password, ipAddress, userAgent, deviceId, cancellationToken);
    }

    private async Task<Result<LoginResult>> LoginInternalAsync(
        UserScope scope,
        Guid? tenantId,
        string email,
        string password,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Result.Failure<LoginResult>(Error.Validation("Auth.InvalidCredentials", "Credenciais inválidas."));

        var utcNow = _clock.UtcNow;
        var normalizedEmail = User.NormalizeEmail(email);
        var rateKey = $"{scope}:{tenantId?.ToString() ?? "saas"}:{normalizedEmail}:{ipAddress ?? "unknown"}";

        if (!_rateLimiter.IsAllowed(rateKey, utcNow))
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.LoginRateLimited, scope, tenantId, null, null,
                normalizedEmail, ipAddress, false, "Tentativa de login bloqueada por rate limiting.", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Failure<LoginResult>(Error.Forbidden("Auth.RateLimited", "Muitas tentativas em curto intervalo. Tente novamente mais tarde."));
        }

        _rateLimiter.RegisterAttempt(rateKey, utcNow);

        var user = await _userRepository.FindByEmailAsync(scope, tenantId, normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.LoginFailed, scope, tenantId, null, user?.Id,
                normalizedEmail, ipAddress, false, "Falha de login (usuário inexistente ou inativo).", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Failure<LoginResult>(Error.Unauthorized("Auth.InvalidCredentials", "E-mail ou senha inválidos."));
        }

        if (user.IsLockedOut(utcNow))
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.LoginLockedOut, scope, tenantId, user.Id, user.Id,
                normalizedEmail, ipAddress, false, "Tentativa de login durante bloqueio temporário por excesso de falhas.", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Failure<LoginResult>(Error.Forbidden("Auth.LockedOut", "Conta temporariamente bloqueada por excesso de tentativas incorretas."));
        }

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            var becameLocked = user.RecordFailedLogin(utcNow, _policy.MaxFailedLoginAttempts, _policy.LockoutDuration);
            var eventType = becameLocked ? SecurityEventType.LoginLockedOut : SecurityEventType.LoginFailed;
            var description = becameLocked
                ? $"Conta bloqueada após {_policy.MaxFailedLoginAttempts} tentativas incorretas."
                : "Senha incorreta.";

            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), eventType, scope, tenantId, user.Id, user.Id,
                normalizedEmail, ipAddress, false, description, utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return becameLocked
                ? Result.Failure<LoginResult>(Error.Forbidden("Auth.LockedOut", "Conta temporariamente bloqueada por excesso de tentativas incorretas."))
                : Result.Failure<LoginResult>(Error.Unauthorized("Auth.InvalidCredentials", "E-mail ou senha inválidos."));
        }

        user.RecordSuccessfulLogin(utcNow);
        _rateLimiter.Reset(rateKey);

        var rawRefreshToken = _tokenGenerator.GenerateUrlSafeToken(32);
        var refreshHash = _tokenGenerator.ComputeSha256Hash(rawRefreshToken);
        var sessionExpiresAt = utcNow.Add(_policy.SessionDuration);

        var session = user.IssueSession(refreshHash, utcNow, sessionExpiresAt, ipAddress, userAgent, deviceId);
        var accessToken = _accessTokenService.IssueAccessToken(user, session.Id);

        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.LoginSucceeded, scope, tenantId, user.Id, user.Id,
            normalizedEmail, ipAddress, true, "Login realizado com sucesso.", utcNow), cancellationToken);

        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new LoginResult(
            user.Id,
            session.Id,
            user.Scope,
            user.TenantId,
            accessToken.Token,
            rawRefreshToken,
            accessToken.ExpiresAtUtc,
            session.ExpiresAtUtc));
    }

    /// <summary>
    /// Valida no backend se o token de acesso corresponde a uma sessão não revogada, não expirada
    /// e com mesmo <c>SecurityStamp</c> do usuário ativo.
    /// </summary>
    public async Task<Result<User>> ValidateActiveAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var claims = _accessTokenService.ValidateAccessToken(accessToken);
        if (claims is null)
            return Result.Failure<User>(Error.Unauthorized("Auth.InvalidToken", "Token inválido ou expirado."));

        var user = await _userRepository.FindBySessionIdAsync(claims.SessionId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<User>(Error.Unauthorized("Auth.SessionInvalid", "Sessão inválida ou usuário inativo."));

        if (!string.Equals(user.SecurityStamp, claims.SecurityStamp, StringComparison.Ordinal))
            return Result.Failure<User>(Error.Unauthorized("Auth.SecurityStampMismatch", "Sessão invalidada por alteração de segurança."));

        var session = user.Sessions.FirstOrDefault(s => s.Id == claims.SessionId);
        if (session is null || !session.IsActive(_clock.UtcNow))
            return Result.Failure<User>(Error.Unauthorized("Auth.SessionRevoked", "Sessão expirada ou revogada."));

        return Result.Success(user);
    }

    public async Task<Result> RevokeSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        string reason,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.FindBySessionIdAsync(sessionId, cancellationToken);
        if (user is null)
            return Result.Failure(Error.NotFound("Session.NotFound", "Sessão não encontrada."));

        var utcNow = _clock.UtcNow;
        var revokeResult = user.RevokeSession(sessionId, utcNow, reason);
        if (revokeResult.IsFailure)
            return revokeResult;

        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.SessionRevoked, user.Scope, user.TenantId, actorUserId, user.Id,
            user.NormalizedEmail, ipAddress, true, $"Sessão {sessionId} revogada: {reason}", utcNow), cancellationToken);

        await _userRepository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
