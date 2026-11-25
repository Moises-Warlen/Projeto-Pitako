using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Security.Infrastructure.Crypto;
using Pitako.Modules.Security.Infrastructure.Persistence;
using Pitako.Modules.Security.Infrastructure.RateLimiting;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class MutableClock(DateTimeOffset initialUtc) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = initialUtc;
}

public sealed class SecurityWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 24, 22, 0, 0, TimeSpan.Zero));
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly DefaultPasswordStrengthValidator _strengthValidator = new();
    private readonly CryptoTokenGenerator _tokenGenerator = new();
    private readonly JwtSettings _jwtSettings = new()
    {
        SigningKey = "pitako_super_secret_integration_test_signing_key_2026!",
        AccessTokenLifetime = TimeSpan.FromMinutes(30)
    };

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private SecurityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SecurityDbContext>().UseSqlite(_connection).Options;
        return new SecurityDbContext(options, _clock);
    }

    private sealed class ServiceScope(
        AuthenticationService auth,
        PasswordRecoveryService recovery,
        UserAuthorizationService users,
        SecurityDbContext db) : IDisposable
    {
        public AuthenticationService Auth { get; } = auth;
        public PasswordRecoveryService Recovery { get; } = recovery;
        public UserAuthorizationService Users { get; } = users;
        public SecurityDbContext Db { get; } = db;
        public void Dispose() => Db.Dispose();
    }

    private ServiceScope CreateServices(
        ITenantContext? tenantContext = null,
        SecurityPolicyOptions? policy = null,
        ILoginRateLimiter? rateLimiter = null)
    {
        var db = CreateDb();
        var userRepo = new EfUserRepository(db);
        var auditRepo = new EfSecurityAuditRepository(db);
        var jwt = new HmacJwtTokenService(_jwtSettings, _clock);
        var effectivePolicy = policy ?? new SecurityPolicyOptions();
        var limiter = rateLimiter ?? new InMemoryLoginRateLimiter(effectivePolicy);

        var auth = new AuthenticationService(userRepo, auditRepo, _hasher, _tokenGenerator, jwt, limiter, _clock, effectivePolicy);
        var recovery = new PasswordRecoveryService(userRepo, auditRepo, _tokenGenerator, _hasher, _strengthValidator, _clock, effectivePolicy);
        var users = new UserAuthorizationService(userRepo, auditRepo, _hasher, _strengthValidator, tenantContext ?? new TenantContext(), _clock);
        return new ServiceScope(auth, recovery, users, db);
    }

    [Fact]
    public async Task Login_Lockout_And_SessionRevocation_ShouldWorkEndToEnd()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var policy = new SecurityPolicyOptions { MaxFailedLoginAttempts = 3, LockoutDuration = TimeSpan.FromMinutes(15), RateLimitMaxAttemptsPerWindow = 20 };
        var limiter = new InMemoryLoginRateLimiter(policy);

        using (var setup = CreateServices(tenantContext, policy, limiter))
        {
            var master = (await setup.Users.CreateSaasUserAsync(null, "Dono Pitako", "master@pitako.com", "Pitako@2026", SaasRole.MasterAdministrator)).Value;
            await setup.Users.CreateEstablishmentUserAsync(master, tenantId, "Caixa Loja", "caixa@loja.com", "Caixa@2026", EstablishmentRole.Cashier);
        }

        // 3 tentativas erradas disparam lockout
        for (var i = 0; i < 3; i++)
        {
            using var attempt = CreateServices(tenantContext, policy, limiter);
            var failed = await attempt.Auth.LoginEstablishmentAsync(tenantId, "caixa@loja.com", "Errada@123", "10.0.0.1");
            Assert.True(failed.IsFailure);
            Assert.Equal(i == 2 ? "Auth.LockedOut" : "Auth.InvalidCredentials", failed.Error.Code);
        }

        // Mesmo com senha correta durante o lockout, deve bloquear
        using (var blocked = CreateServices(tenantContext, policy, limiter))
        {
            var res = await blocked.Auth.LoginEstablishmentAsync(tenantId, "caixa@loja.com", "Caixa@2026", "10.0.0.1");
            Assert.True(res.IsFailure);
            Assert.Equal("Auth.LockedOut", res.Error.Code);
        }

        // Avança 16 minutos: lockout expira e login funciona
        _clock.UtcNow = _clock.UtcNow.AddMinutes(16);
        LoginResult loginOk;
        using (var ok = CreateServices(tenantContext, policy, limiter))
        {
            var res = await ok.Auth.LoginEstablishmentAsync(tenantId, "caixa@loja.com", "Caixa@2026", "10.0.0.1");
            Assert.True(res.IsSuccess);
            loginOk = res.Value;

            var validUser = await ok.Auth.ValidateActiveAccessTokenAsync(loginOk.AccessToken);
            Assert.True(validUser.IsSuccess);
        }

        // Revogação da sessão invalida o access token imediatamente
        using (var logout = CreateServices(tenantContext, policy, limiter))
        {
            var revoke = await logout.Auth.RevokeSessionAsync(loginOk.SessionId, loginOk.UserId, "Logout manual");
            Assert.True(revoke.IsSuccess);

            var afterLogout = await logout.Auth.ValidateActiveAccessTokenAsync(loginOk.AccessToken);
            Assert.True(afterLogout.IsFailure);
            Assert.Equal("Auth.SessionRevoked", afterLogout.Error.Code);
        }
    }

    [Fact]
    public async Task PasswordRecovery_ShouldWorkForMasterAndStoreAdmin_InvalidateSessions_AndPreventReuse()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        using (var setup = CreateServices(tenantContext))
        {
            var master = (await setup.Users.CreateSaasUserAsync(null, "Master", "master@pitako.com", "Master@2026", SaasRole.MasterAdministrator)).Value;
            await setup.Users.CreateEstablishmentUserAsync(master, tenantId, "Admin Loja", "admin@loja.com", "Loja@2026", EstablishmentRole.Administrator);
        }

        // Login do Master e do Admin da loja antes da recuperação
        LoginResult masterSession;
        LoginResult storeSession;
        using (var logins = CreateServices(tenantContext))
        {
            masterSession = (await logins.Auth.LoginSaasAsync("master@pitako.com", "Master@2026")).Value;
            storeSession = (await logins.Auth.LoginEstablishmentAsync(tenantId, "admin@loja.com", "Loja@2026")).Value;
        }

        // Solicita recuperação para e-mail inexistente: resposta neutra sem link
        using (var unknown = CreateServices(tenantContext))
        {
            var req = await unknown.Recovery.RequestSaasPasswordResetAsync("naoexiste@pitako.com", "https://app.pitako.com/reset");
            Assert.True(req.Accepted);
            Assert.Null(req.GeneratedSecureLink);
        }

        // Recuperação completa do Master
        string masterToken;
        using (var reqMaster = CreateServices(tenantContext))
        {
            var res = await reqMaster.Recovery.RequestSaasPasswordResetAsync("master@pitako.com", "https://app.pitako.com/master/reset");
            Assert.True(res.Accepted);
            Assert.NotNull(res.GeneratedSecureLink);
            masterToken = Uri.UnescapeDataString(res.GeneratedSecureLink!.Split("token=")[1]);
        }

        using (var completeMaster = CreateServices(tenantContext))
        {
            var done = await completeMaster.Recovery.CompletePasswordResetAsync(masterToken, "NovaMaster@2026");
            Assert.True(done.IsSuccess);

            // Sessão anterior do Master foi invalidada
            var oldSessionCheck = await completeMaster.Auth.ValidateActiveAccessTokenAsync(masterSession.AccessToken);
            Assert.True(oldSessionCheck.IsFailure);

            // Reutilizar mesmo token falha
            var reuse = await completeMaster.Recovery.CompletePasswordResetAsync(masterToken, "OutraMaster@2026");
            Assert.True(reuse.IsFailure);
            Assert.Equal("PasswordReset.TokenAlreadyUsed", reuse.Error.Code);

            // Login com nova senha funciona
            var newLogin = await completeMaster.Auth.LoginSaasAsync("master@pitako.com", "NovaMaster@2026");
            Assert.True(newLogin.IsSuccess);
        }

        // Recuperação completa do Admin da loja
        string storeToken;
        using (var reqStore = CreateServices(tenantContext))
        {
            var res = await reqStore.Recovery.RequestEstablishmentPasswordResetAsync(tenantId, "admin@loja.com", "https://app.pitako.com/reset");
            Assert.NotNull(res.GeneratedSecureLink);
            storeToken = Uri.UnescapeDataString(res.GeneratedSecureLink!.Split("token=")[1]);
        }

        using (var completeStore = CreateServices(tenantContext))
        {
            var done = await completeStore.Recovery.CompletePasswordResetAsync(storeToken, "NovaLoja@2026");
            Assert.True(done.IsSuccess);

            var oldStoreSession = await completeStore.Auth.ValidateActiveAccessTokenAsync(storeSession.AccessToken);
            Assert.True(oldStoreSession.IsFailure);

            var newStoreLogin = await completeStore.Auth.LoginEstablishmentAsync(tenantId, "admin@loja.com", "NovaLoja@2026");
            Assert.True(newStoreLogin.IsSuccess);
        }
    }

    [Fact]
    public async Task CustomModulePermissions_ShouldPersistGranularActions()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid attendantId;
        using (var svc = CreateServices(tenantContext))
        {
            var master = (await svc.Users.CreateSaasUserAsync(null, "Master", "master@pitako.com", "Master@2026", SaasRole.MasterAdministrator)).Value;
            var storeAdmin = (await svc.Users.CreateEstablishmentUserAsync(master, tenantId, "Admin", "admin@loja.com", "Admin@2026", EstablishmentRole.Administrator)).Value;
            var attendant = (await svc.Users.CreateEstablishmentUserAsync(storeAdmin, tenantId, "Atendente", "atendente@loja.com", "Atend@2026", EstablishmentRole.Attendant)).Value;
            attendantId = attendant.Id;

            var update = await svc.Users.UpdateEstablishmentUserPermissionsAsync(storeAdmin, attendantId,
            [
                new ModulePermissionGrant(SystemModules.Orders, PermissionAction.View | PermissionAction.Create | PermissionAction.Cancel),
                new ModulePermissionGrant(SystemModules.Reports, PermissionAction.View | PermissionAction.Export)
            ]);
            Assert.True(update.IsSuccess);
        }

        using (var verify = CreateServices(tenantContext))
        {
            var repo = new EfUserRepository(verify.Db);
            var loaded = await repo.FindByIdAsync(attendantId);
            Assert.NotNull(loaded);
            Assert.True(loaded!.HasPermission(SystemModules.Orders, PermissionAction.Cancel));
            Assert.True(loaded.HasPermission(SystemModules.Reports, PermissionAction.Export));
            Assert.False(loaded.HasPermission(SystemModules.Orders, PermissionAction.Delete));
        }
    }
}
