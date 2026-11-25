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

namespace Pitako.SecurityTests;

public sealed class SecurityAuthorizationBoundaryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly DefaultPasswordStrengthValidator _strength = new();
    private readonly CryptoTokenGenerator _tokens = new();

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

    [Fact]
    public async Task EstablishmentAdmin_ShouldNeverManageOtherTenantUsers_OrAccessSaasMaster()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);

        await using var db = CreateDb();
        var userRepo = new EfUserRepository(db);
        var auditRepo = new EfSecurityAuditRepository(db);
        var userSvcForA = new UserAuthorizationService(userRepo, auditRepo, _hasher, _strength, ctxA, _clock);

        var master = (await userSvcForA.CreateSaasUserAsync(null, "Master", "master@pitako.com", "Master@2026", SaasRole.MasterAdministrator)).Value;
        var adminA = (await userSvcForA.CreateEstablishmentUserAsync(master, tenantA, "Admin A", "admin@a.com", "AdminA@2026", EstablishmentRole.Administrator)).Value;
        var userB = (await userSvcForA.CreateEstablishmentUserAsync(master, tenantB, "Caixa B", "caixa@b.com", "CaixaB@2026", EstablishmentRole.Cashier)).Value;

        // 1. Admin A não pode criar usuário na Loja B
        await Assert.ThrowsAsync<CrossTenantAccessException>(() =>
            userSvcForA.CreateEstablishmentUserAsync(adminA, tenantB, "Invasor", "inv@b.com", "Inv@2026", EstablishmentRole.Cashier));

        // 2. Admin A não pode alterar permissões de usuário da Loja B
        await Assert.ThrowsAsync<CrossTenantAccessException>(() =>
            userSvcForA.UpdateEstablishmentUserPermissionsAsync(adminA, userB.Id,
                [new ModulePermissionGrant(SystemModules.Orders, PermissionAction.All)]));

        // 3. Admin A não pode criar usuário SaaS nem alterar permissões do Master
        var createSaas = await userSvcForA.CreateSaasUserAsync(adminA, "Fake Master", "fake@pitako.com", "Fake@2026", SaasRole.MasterAdministrator);
        Assert.True(createSaas.IsFailure);
        Assert.Equal("Security.MasterAccessDenied", createSaas.Error.Code);

        var editMaster = await userSvcForA.UpdateEstablishmentUserPermissionsAsync(adminA, master.Id,
            [new ModulePermissionGrant(SystemModules.Orders, PermissionAction.View)]);
        Assert.True(editMaster.IsFailure);
        Assert.Equal("Security.MasterAccessDenied", editMaster.Error.Code);

        // 4. Admin A não pode conceder permissão do Painel Master para funcionário da própria loja
        var grantMasterModule = await userSvcForA.UpdateEstablishmentUserPermissionsAsync(adminA, adminA.Id,
            [new ModulePermissionGrant(SystemModules.MasterPanel, PermissionAction.View)]);
        Assert.True(grantMasterModule.IsFailure);
        Assert.Equal("Permissions.SaasModuleForbidden", grantMasterModule.Error.Code);

        // 5. Auditoria deve conter tentativas indevidas registradas
        var auditsA = await auditRepo.ListByTenantAsync(tenantA);
        Assert.Contains(auditsA, a => a.EventType == SecurityEventType.UnauthorizedMasterAccessAttempt);
    }

    [Fact]
    public async Task LoginRateLimiter_ShouldBlockExcessiveAttemptsAndAuditEvent()
    {
        var tenantId = Guid.NewGuid();
        var policy = new SecurityPolicyOptions { RateLimitMaxAttemptsPerWindow = 3, RateLimitWindow = TimeSpan.FromMinutes(5), MaxFailedLoginAttempts = 10 };
        var rateLimiter = new InMemoryLoginRateLimiter(policy);
        var jwt = new HmacJwtTokenService(new JwtSettings { SigningKey = "pitako_super_secret_security_test_signing_key_2026!" }, _clock);

        await using var db = CreateDb();
        var userRepo = new EfUserRepository(db);
        var auditRepo = new EfSecurityAuditRepository(db);
        var auth = new AuthenticationService(userRepo, auditRepo, _hasher, _tokens, jwt, rateLimiter, _clock, policy);

        for (var i = 0; i < 3; i++)
        {
            var res = await auth.LoginEstablishmentAsync(tenantId, "alvo@loja.com", "Errada@123", "192.168.0.50");
            Assert.Equal("Auth.InvalidCredentials", res.Error.Code);
        }

        var rateLimited = await auth.LoginEstablishmentAsync(tenantId, "alvo@loja.com", "Errada@123", "192.168.0.50");
        Assert.True(rateLimited.IsFailure);
        Assert.Equal("Auth.RateLimited", rateLimited.Error.Code);

        var logs = await auditRepo.ListByTenantAsync(tenantId);
        Assert.Contains(logs, l => l.EventType == SecurityEventType.LoginRateLimited);
    }
}
