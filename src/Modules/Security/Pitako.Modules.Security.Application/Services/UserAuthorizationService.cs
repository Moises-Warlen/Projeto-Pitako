using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Security.Application.Services;

/// <summary>
/// Gestão de usuários e permissões com isolamento rigoroso entre níveis SaaS e Estabelecimentos (Seção 10).
/// </summary>
public sealed class UserAuthorizationService
{
    private readonly IUserRepository _userRepository;
    private readonly ISecurityAuditRepository _auditRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordStrengthValidator _passwordStrengthValidator;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public UserAuthorizationService(
        IUserRepository userRepository,
        ISecurityAuditRepository auditRepository,
        IPasswordHasher passwordHasher,
        IPasswordStrengthValidator passwordStrengthValidator,
        ITenantContext tenantContext,
        IClock clock)
    {
        _userRepository = userRepository;
        _auditRepository = auditRepository;
        _passwordHasher = passwordHasher;
        _passwordStrengthValidator = passwordStrengthValidator;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<User>> CreateSaasUserAsync(
        User? actor,
        string fullName,
        string email,
        string initialPassword,
        SaasRole role,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;

        if (actor is not null)
        {
            if (actor.Scope != UserScope.Saas || !actor.HasPermission(SystemModules.MasterPanel, PermissionAction.Administer))
            {
                await _auditRepository.AddAsync(new SecurityAuditLog(
                    Guid.NewGuid(), SecurityEventType.UnauthorizedMasterAccessAttempt, actor.Scope, actor.TenantId,
                    actor.Id, null, actor.NormalizedEmail, ipAddress, false,
                    "Tentativa não autorizada de criar usuário SaaS.", utcNow), cancellationToken);
                await _userRepository.SaveChangesAsync(cancellationToken);

                return Result.Failure<User>(Error.Forbidden("Security.MasterAccessDenied", "Somente administradores Master podem administrar usuários SaaS."));
            }
        }

        var passwordValidation = _passwordStrengthValidator.Validate(initialPassword);
        if (passwordValidation.IsFailure)
            return Result.Failure<User>(passwordValidation.Error);

        var normalizedEmail = User.NormalizeEmail(email);
        var existing = await _userRepository.FindByEmailAsync(UserScope.Saas, null, normalizedEmail, cancellationToken);
        if (existing is not null)
            return Result.Failure<User>(Error.Conflict("User.EmailAlreadyExists", "Já existe um usuário SaaS com este e-mail."));

        var userResult = User.CreateSaasUser(fullName, email, _passwordHasher.HashPassword(initialPassword), role);
        if (userResult.IsFailure)
            return userResult;

        var user = userResult.Value;
        await _userRepository.AddAsync(user, cancellationToken);
        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.UserCreated, UserScope.Saas, null, actor?.Id, user.Id,
            user.NormalizedEmail, ipAddress, true, $"Usuário SaaS criado com perfil {role}.", utcNow), cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(user);
    }

    public async Task<Result<User>> CreateEstablishmentUserAsync(
        User actor,
        Guid targetTenantId,
        string fullName,
        string email,
        string initialPassword,
        EstablishmentRole role,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var utcNow = _clock.UtcNow;

        if (actor.Scope == UserScope.Establishment)
        {
            TenantGuard.EnsureOwnership(_tenantContext, targetTenantId);
            if (actor.TenantId != targetTenantId)
            {
                await RecordCrossTenantAttemptAsync(actor, targetTenantId, "Tentativa de criar usuário em outro estabelecimento.", ipAddress, utcNow, cancellationToken);
                throw new CrossTenantAccessException(actor.TenantId, targetTenantId, "Administrador de uma loja não pode criar usuários em outra loja.");
            }
        }

        if (!actor.HasPermission(SystemModules.Security, PermissionAction.Create))
            return Result.Failure<User>(Error.Forbidden("Security.CreateUserForbidden", "Permissão insuficiente para cadastrar usuários."));

        var passwordValidation = _passwordStrengthValidator.Validate(initialPassword);
        if (passwordValidation.IsFailure)
            return Result.Failure<User>(passwordValidation.Error);

        var normalizedEmail = User.NormalizeEmail(email);
        var existing = await _userRepository.FindByEmailAsync(UserScope.Establishment, targetTenantId, normalizedEmail, cancellationToken);
        if (existing is not null)
            return Result.Failure<User>(Error.Conflict("User.EmailAlreadyExists", "Já existe um usuário com este e-mail neste estabelecimento."));

        var userResult = User.CreateEstablishmentUser(targetTenantId, fullName, email, _passwordHasher.HashPassword(initialPassword), role);
        if (userResult.IsFailure)
            return userResult;

        var user = userResult.Value;
        await _userRepository.AddAsync(user, cancellationToken);
        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.UserCreated, UserScope.Establishment, targetTenantId, actor.Id, user.Id,
            user.NormalizedEmail, ipAddress, true, $"Usuário da loja criado com perfil {role}.", utcNow), cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(user);
    }

    public async Task<Result> UpdateEstablishmentUserPermissionsAsync(
        User actor,
        Guid targetUserId,
        IEnumerable<ModulePermissionGrant> grants,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var utcNow = _clock.UtcNow;

        var targetUser = await _userRepository.FindByIdAsync(targetUserId, cancellationToken);
        if (targetUser is null)
            return Result.Failure(Error.NotFound("User.NotFound", "Usuário alvo não encontrado."));

        if (targetUser.Scope != UserScope.Establishment || targetUser.TenantId is null)
        {
            await _auditRepository.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(), SecurityEventType.UnauthorizedMasterAccessAttempt, actor.Scope, actor.TenantId,
                actor.Id, targetUser.Id, actor.NormalizedEmail, ipAddress, false,
                "Tentativa de alterar permissões de usuário SaaS via fluxo de estabelecimento.", utcNow), cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Failure(Error.Forbidden("Security.MasterAccessDenied", "Usuário de estabelecimento não pode administrar usuários SaaS."));
        }

        if (actor.Scope == UserScope.Establishment)
        {
            TenantGuard.EnsureOwnership(_tenantContext, targetUser.TenantId.Value);
            if (actor.TenantId != targetUser.TenantId)
            {
                await RecordCrossTenantAttemptAsync(actor, targetUser.TenantId.Value, "Tentativa de alterar permissões de usuário de outro estabelecimento.", ipAddress, utcNow, cancellationToken);
                throw new CrossTenantAccessException(actor.TenantId, targetUser.TenantId, "Administrador de uma loja não pode administrar usuários de outra loja.");
            }
        }

        if (!actor.HasPermission(SystemModules.Security, PermissionAction.Administer) &&
            !actor.HasPermission(SystemModules.Security, PermissionAction.Edit))
        {
            return Result.Failure(Error.Forbidden("Security.PermissionEditForbidden", "Permissão insuficiente para editar permissões."));
        }

        var result = targetUser.SetCustomModulePermissions(grants, utcNow);
        if (result.IsFailure)
            return result;

        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.PermissionsUpdated, UserScope.Establishment, targetUser.TenantId,
            actor.Id, targetUser.Id, targetUser.NormalizedEmail, ipAddress, true,
            "Permissões granulares por módulo atualizadas.", utcNow), cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task RecordCrossTenantAttemptAsync(
        User actor,
        Guid targetTenantId,
        string description,
        string? ipAddress,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await _auditRepository.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(), SecurityEventType.CrossTenantAttemptBlocked, actor.Scope, actor.TenantId,
            actor.Id, null, actor.NormalizedEmail, ipAddress, false,
            $"{description} TargetTenant={targetTenantId}", utcNow), cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);
    }
}
