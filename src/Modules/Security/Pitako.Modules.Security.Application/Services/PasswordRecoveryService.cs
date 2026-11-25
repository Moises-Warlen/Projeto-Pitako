using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Security.Application.Services;

/// <summary>
/// Resultado da solicitação de recuperação de senha.
/// <see cref="GeneratedSecureLink"/> é destinado apenas ao canal de entrega oficial (e-mail/notificação), nunca exposto em resposta pública sem autenticação.
/// </summary>
public sealed record PasswordResetRequestResult(
    bool Accepted,
    string? GeneratedSecureLink,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Caso de uso de recuperação de senha para administradores Master (SaaS) e usuários/administradores de estabelecimentos (Seção 9).
/// </summary>
public sealed class PasswordRecoveryService
{
    private readonly IUserRepository _userRepository;
    private readonly ISecurityAuditRepository _auditRepository;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordStrengthValidator _passwordStrengthValidator;
    private readonly IClock _clock;
    private readonly SecurityPolicyOptions _policy;

    public PasswordRecoveryService(
        IUserRepository userRepository,
        ISecurityAuditRepository auditRepository,
        ITokenGenerator tokenGenerator,
        IPasswordHasher passwordHasher,
        IPasswordStrengthValidator passwordStrengthValidator,
        IClock clock,
        SecurityPolicyOptions? policy = null)
    {
        _userRepository = userRepository;
        _auditRepository = auditRepository;
        _tokenGenerator = tokenGenerator;
        _passwordHasher = passwordHasher;
        _passwordStrengthValidator = passwordStrengthValidator;
        _clock = clock;
        _policy = policy ?? new SecurityPolicyOptions();
    }

    public Task<PasswordResetRequestResult> RequestSaasPasswordResetAsync(
        string email,
        string resetBaseUrl,
        string? ipAddress = null,
        CancellationToken cancellationToken = default) =>
        RequestResetInternalAsync(UserScope.Saas, null, email, resetBaseUrl, ipAddress, cancellationToken);

    public Task<PasswordResetRequestResult> RequestEstablishmentPasswordResetAsync(
        Guid tenantId,
        string email,
        string resetBaseUrl,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return Task.FromResult(new PasswordResetRequestResult(false, null, null));

        return RequestResetInternalAsync(UserScope.Establishment, tenantId, email, resetBaseUrl, ipAddress, cancellationToken);
    }

    private async Task<PasswordResetRequestResult> RequestResetInternalAsync(
        UserScope scope,
        Guid? tenantId,
        string email,
        string resetBaseUrl,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new PasswordResetRequestResult(true, null, null);

        var utcNow = _clock.UtcNow;
        var normalizedEmail = User.NormalizeEmail(email);
        var user = await _userRepository.FindByEmailAsync(scope, tenantId, normalizedEmail, cancellationToken);

        // Resposta neutra evita enumeração de contas existentes no SaaS ou nas lojas.
        if (user is null || !user.IsActive)
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.PasswordResetRequested, scope, tenantId, null, user?.Id,
                normalizedEmail, ipAddress, false, "Solicitação de recuperação ignorada (e-mail não elegível).", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return new PasswordResetRequestResult(true, null, null);
        }

        var rawToken = _tokenGenerator.GenerateUrlSafeToken(32);
        var tokenHash = _tokenGenerator.ComputeSha256Hash(rawToken);
        var tokenEntity = user.IssuePasswordResetToken(tokenHash, utcNow, _policy.PasswordResetTokenLifetime, ipAddress);

        var separator = resetBaseUrl.Contains('?') ? "&" : "?";
        var secureLink = $"{resetBaseUrl.TrimEnd('/')}{separator}token={Uri.EscapeDataString(rawToken)}";

        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.PasswordResetRequested, scope, tenantId, user.Id, user.Id,
            normalizedEmail, ipAddress, true, "Token único de recuperação de senha gerado.", utcNow), cancellationToken);

        await _userRepository.SaveChangesAsync(cancellationToken);

        return new PasswordResetRequestResult(true, secureLink, tokenEntity.ExpiresAtUtc);
    }

    /// <summary>
    /// Conclui a recuperação validando força da senha, uso único e expiração do token,
    /// invalidando imediatamente todas as sessões ativas do usuário.
    /// </summary>
    public async Task<Result> CompletePasswordResetAsync(
        string rawToken,
        string newPassword,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return Result.Failure(Error.Validation("PasswordReset.InvalidToken", "Token de recuperação inválido."));

        var strengthResult = _passwordStrengthValidator.Validate(newPassword);
        if (strengthResult.IsFailure)
            return strengthResult;

        var utcNow = _clock.UtcNow;
        var tokenHash = _tokenGenerator.ComputeSha256Hash(rawToken);
        var user = await _userRepository.FindByPasswordResetTokenHashAsync(tokenHash, cancellationToken);

        if (user is null)
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.PasswordResetRejected, null, null, null, null,
                null, ipAddress, false, "Tentativa de redefinição com token inexistente.", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Failure(Error.Validation("PasswordReset.InvalidToken", "Token de recuperação inválido ou expirado."));
        }

        var newPasswordHash = _passwordHasher.HashPassword(newPassword);
        var resetResult = user.CompletePasswordReset(tokenHash, newPasswordHash, utcNow);

        if (resetResult.IsFailure)
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.PasswordResetRejected, user.Scope, user.TenantId, user.Id, user.Id,
                user.NormalizedEmail, ipAddress, false, $"Redefinição rejeitada: {resetResult.Error.Code}", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return resetResult;
        }

        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.PasswordResetCompleted, user.Scope, user.TenantId, user.Id, user.Id,
            user.NormalizedEmail, ipAddress, true, "Senha redefinida com token único e todas as sessões anteriores revogadas.", utcNow), cancellationToken);

        await _userRepository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
