using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Security.Domain;

/// <summary>
/// Entrada para definição granular de permissões por módulo em um usuário.
/// </summary>
public sealed record ModulePermissionGrant(string ModuleCode, PermissionAction Actions);

/// <summary>
/// Agregado raiz de identidade de usuário (SaaS ou Estabelecimento) com suporte a lockout,
/// sessões revogáveis, recuperação de senha de uso único e permissões granulares por módulo.
/// </summary>
public sealed class User : AggregateRoot<Guid>, IAuditableEntity
{
    private readonly List<UserModulePermission> _modulePermissions = new();
    private readonly List<UserSession> _sessions = new();
    private readonly List<PasswordResetToken> _passwordResetTokens = new();

    private User()
    {
        FullName = string.Empty;
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        PasswordHash = string.Empty;
        SecurityStamp = string.Empty;
    }

    private User(
        Guid id,
        UserScope scope,
        Guid? tenantId,
        string fullName,
        string email,
        string passwordHash,
        SaasRole? saasRole,
        EstablishmentRole? establishmentRole) : base(id)
    {
        Scope = scope;
        TenantId = tenantId;
        FullName = fullName;
        Email = email;
        NormalizedEmail = NormalizeEmail(email);
        PasswordHash = passwordHash;
        SaasRole = saasRole;
        EstablishmentRole = establishmentRole;
        IsActive = true;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public UserScope Scope { get; private set; }
    public Guid? TenantId { get; private set; }
    public string FullName { get; private set; }
    public string Email { get; private set; }
    public string NormalizedEmail { get; private set; }
    public string PasswordHash { get; private set; }
    public SaasRole? SaasRole { get; private set; }
    public EstablishmentRole? EstablishmentRole { get; private set; }
    public bool IsActive { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public string SecurityStamp { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserModulePermission> ModulePermissions => _modulePermissions.AsReadOnly();
    public IReadOnlyCollection<UserSession> Sessions => _sessions.AsReadOnly();
    public IReadOnlyCollection<PasswordResetToken> PasswordResetTokens => _passwordResetTokens.AsReadOnly();

    /// <summary>
    /// Cria um usuário administrativo da operadora PITAKO (Painel Master / SaaS).
    /// </summary>
    public static Result<User> CreateSaasUser(string fullName, string email, string passwordHash, SaasRole role)
    {
        var baseValidation = ValidateCommonFields(fullName, email, passwordHash);
        if (baseValidation.IsFailure)
            return Result.Failure<User>(baseValidation.Error);

        return Result.Success(new User(
            Guid.NewGuid(),
            UserScope.Saas,
            tenantId: null,
            fullName.Trim(),
            email.Trim(),
            passwordHash,
            role,
            establishmentRole: null));
    }

    /// <summary>
    /// Cria um usuário vinculado exclusivamente a um estabelecimento cliente.
    /// </summary>
    public static Result<User> CreateEstablishmentUser(
        Guid tenantId,
        string fullName,
        string email,
        string passwordHash,
        EstablishmentRole role)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<User>(Error.Validation("User.TenantRequired", "Usuário de estabelecimento exige TenantId válido."));

        var baseValidation = ValidateCommonFields(fullName, email, passwordHash);
        if (baseValidation.IsFailure)
            return Result.Failure<User>(baseValidation.Error);

        return Result.Success(new User(
            Guid.NewGuid(),
            UserScope.Establishment,
            tenantId,
            fullName.Trim(),
            email.Trim(),
            passwordHash,
            saasRole: null,
            role));
    }

    public bool IsLockedOut(DateTimeOffset utcNow) =>
        LockoutEndUtc.HasValue && LockoutEndUtc.Value > utcNow;

    /// <summary>
    /// Zera contadores de proteção contra força bruta após autenticação bem-sucedida.
    /// </summary>
    public void RecordSuccessfulLogin(DateTimeOffset utcNow)
    {
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// Incrementa falhas de login e ativa bloqueio temporário ao atingir o limite configurado.
    /// </summary>
    public bool RecordFailedLogin(DateTimeOffset utcNow, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        if (maxFailedAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFailedAttempts));

        // Reinicia janela caso um lockout anterior já tenha expirado.
        if (LockoutEndUtc.HasValue && LockoutEndUtc.Value <= utcNow)
        {
            FailedLoginAttempts = 0;
            LockoutEndUtc = null;
        }

        FailedLoginAttempts++;
        UpdatedAtUtc = utcNow;

        if (FailedLoginAttempts >= maxFailedAttempts)
        {
            LockoutEndUtc = utcNow.Add(lockoutDuration);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Define permissões granulares por módulo. Impede atribuição de módulos SaaS para usuários de loja.
    /// </summary>
    public Result SetCustomModulePermissions(IEnumerable<ModulePermissionGrant> grants, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var grantList = grants.ToList();
        foreach (var grant in grantList)
        {
            var normalized = SystemModules.Normalize(grant.ModuleCode);
            if (string.IsNullOrWhiteSpace(normalized))
                return Result.Failure(Error.Validation("Permissions.InvalidModule", "Código de módulo inválido."));

            if (Scope == UserScope.Establishment && SystemModules.IsSaasOnlyModule(normalized))
            {
                return Result.Failure(Error.Forbidden(
                    "Permissions.SaasModuleForbidden",
                    "Usuários de estabelecimento não podem receber permissões do Painel Master."));
            }
        }

        _modulePermissions.Clear();
        foreach (var grant in grantList)
        {
            var normalized = SystemModules.Normalize(grant.ModuleCode);
            var existing = _modulePermissions.FirstOrDefault(x => x.ModuleCode == normalized);
            if (existing is not null)
                existing.UpdateActions(grant.Actions);
            else
                _modulePermissions.Add(new UserModulePermission(Guid.NewGuid(), Id, normalized, grant.Actions));
        }

        SecurityStamp = Guid.NewGuid().ToString("N");
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Avalia se o usuário ativo possui a ação solicitada no módulo informado,
    /// aplicando o menor privilégio e bloqueando acesso cruzado entre níveis SaaS/Loja.
    /// </summary>
    public bool HasPermission(string moduleCode, PermissionAction requiredAction)
    {
        if (!IsActive || requiredAction == PermissionAction.None)
            return false;

        var normalized = SystemModules.Normalize(moduleCode);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        // Regra inegociável da Seção 10: usuário de estabelecimento jamais acessa Painel Master.
        if (Scope == UserScope.Establishment && SystemModules.IsSaasOnlyModule(normalized))
            return false;

        var custom = _modulePermissions.FirstOrDefault(p => p.ModuleCode == normalized);
        var effectiveActions = custom is not null
            ? custom.AllowedActions
            : GetRoleDefaultActions(normalized);

        return (effectiveActions & requiredAction) == requiredAction;
    }

    /// <summary>
    /// Emite nova sessão vinculada ao escopo do usuário.
    /// </summary>
    public UserSession IssueSession(
        string refreshTokenHash,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        string? ipAddress,
        string? userAgent,
        string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash))
            throw new ArgumentException("Hash do refresh token é obrigatório.", nameof(refreshTokenHash));

        var session = new UserSession(
            Guid.NewGuid(),
            Id,
            TenantId,
            refreshTokenHash,
            issuedAtUtc,
            expiresAtUtc,
            ipAddress,
            userAgent,
            deviceId);

        _sessions.Add(session);
        return session;
    }

    public Result RevokeSession(Guid sessionId, DateTimeOffset utcNow, string reason)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
            return Result.Failure(Error.NotFound("Session.NotFound", "Sessão não encontrada."));

        session.Revoke(utcNow, reason);
        return Result.Success();
    }

    public int RevokeAllSessions(DateTimeOffset utcNow, string reason)
    {
        var count = 0;
        foreach (var session in _sessions.Where(s => s.RevokedAtUtc is null))
        {
            session.Revoke(utcNow, reason);
            count++;
        }

        SecurityStamp = Guid.NewGuid().ToString("N");
        UpdatedAtUtc = utcNow;
        return count;
    }

    /// <summary>
    /// Registra novo token de recuperação de senha (hash SHA-256), invalidando tokens pendentes anteriores.
    /// </summary>
    public PasswordResetToken IssuePasswordResetToken(
        string tokenHash,
        DateTimeOffset createdAtUtc,
        TimeSpan lifetime,
        string? requestedFromIp)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash é obrigatório.", nameof(tokenHash));

        foreach (var pending in _passwordResetTokens)
            pending.Invalidate(createdAtUtc);

        var token = new PasswordResetToken(
            Guid.NewGuid(),
            Id,
            tokenHash,
            createdAtUtc,
            createdAtUtc.Add(lifetime),
            requestedFromIp);

        _passwordResetTokens.Add(token);
        return token;
    }

    /// <summary>
    /// Valida token de uso único, atualiza a senha e revoga todas as sessões ativas do usuário (Seção 9).
    /// </summary>
    public Result CompletePasswordReset(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset utcNow)
    {
        if (!IsActive)
            return Result.Failure(Error.Forbidden("User.Inactive", "Usuário inativo não pode redefinir senha."));

        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(Error.Validation("Password.Empty", "Nova senha é obrigatória."));

        var token = _passwordResetTokens.FirstOrDefault(t => string.Equals(t.TokenHash, tokenHash, StringComparison.Ordinal));
        if (token is null)
            return Result.Failure(Error.Validation("PasswordReset.InvalidToken", "Token de recuperação inválido."));

        if (token.UsedAtUtc.HasValue)
            return Result.Failure(Error.Validation("PasswordReset.TokenAlreadyUsed", "Token de recuperação já foi utilizado."));

        if (token.InvalidatedAtUtc.HasValue || token.ExpiresAtUtc <= utcNow)
            return Result.Failure(Error.Validation("PasswordReset.TokenExpired", "Token de recuperação expirado ou invalidado."));

        token.MarkUsed(utcNow);
        foreach (var other in _passwordResetTokens.Where(t => t.Id != token.Id))
            other.Invalidate(utcNow);

        PasswordHash = newPasswordHash;
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        RevokeAllSessions(utcNow, "Redefinição de senha concluída");

        return Result.Success();
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        IsActive = false;
        RevokeAllSessions(utcNow, "Usuário desativado");
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private PermissionAction GetRoleDefaultActions(string normalizedModule)
    {
        if (Scope == UserScope.Saas && SaasRole.HasValue)
            return RolePermissionCatalog.GetDefaultPermissions(SaasRole.Value, normalizedModule);

        if (Scope == UserScope.Establishment && EstablishmentRole.HasValue)
            return RolePermissionCatalog.GetDefaultPermissions(EstablishmentRole.Value, normalizedModule);

        return PermissionAction.None;
    }

    private static Result ValidateCommonFields(string fullName, string email, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Result.Failure(Error.Validation("User.NameRequired", "Nome completo é obrigatório."));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
            return Result.Failure(Error.Validation("User.InvalidEmail", "E-mail inválido."));

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure(Error.Validation("User.PasswordHashRequired", "Hash de senha é obrigatório."));

        return Result.Success();
    }

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}
