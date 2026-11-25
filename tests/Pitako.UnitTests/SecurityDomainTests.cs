using Pitako.Modules.Security.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class SecurityDomainTests
{
    [Fact]
    public void RolePermissionCatalog_ShouldEnforceLeastPrivilegeAndBlockMasterAccessForEstablishments()
    {
        var tenantId = Guid.NewGuid();
        var storeAdmin = User.CreateEstablishmentUser(tenantId, "Admin Loja", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchen = User.CreateEstablishmentUser(tenantId, "Cozinha", "cozinha@loja.com", "hash", EstablishmentRole.Kitchen).Value;
        var master = User.CreateSaasUser("Master", "master@pitako.com.br", "hash", SaasRole.MasterAdministrator).Value;

        Assert.True(master.HasPermission(SystemModules.MasterPanel, PermissionAction.Administer));
        Assert.False(storeAdmin.HasPermission(SystemModules.MasterPanel, PermissionAction.View));
        Assert.True(storeAdmin.HasPermission(SystemModules.Orders, PermissionAction.Cancel));

        Assert.True(kitchen.HasPermission(SystemModules.Orders, PermissionAction.View));
        Assert.False(kitchen.HasPermission(SystemModules.Orders, PermissionAction.Cancel));
        Assert.False(kitchen.HasPermission(SystemModules.Finance, PermissionAction.View));
    }

    [Fact]
    public void SetCustomModulePermissions_ShouldRejectMasterPanel_ForEstablishmentUsers()
    {
        var user = User.CreateEstablishmentUser(Guid.NewGuid(), "Gerente", "gerente@loja.com", "hash", EstablishmentRole.Manager).Value;
        var result = user.SetCustomModulePermissions(
            [new ModulePermissionGrant(SystemModules.MasterPanel, PermissionAction.View)],
            DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Permissions.SaasModuleForbidden", result.Error.Code);
    }

    [Fact]
    public void PasswordReset_ShouldBeSingleUse_CheckExpiration_AndRevokeAllSessions()
    {
        var now = new DateTimeOffset(2026, 11, 24, 22, 0, 0, TimeSpan.Zero);
        var user = User.CreateEstablishmentUser(Guid.NewGuid(), "Caixa", "caixa@loja.com", "old_hash", EstablishmentRole.Cashier).Value;

        var session = user.IssueSession("refresh_hash_1", now, now.AddHours(8), "127.0.0.1", "Test", "dev-1");
        Assert.True(session.IsActive(now.AddMinutes(1)));

        user.IssuePasswordResetToken("token_hash_1", now, TimeSpan.FromMinutes(30), "127.0.0.1");

        // Tentativa após expiração deve falhar
        var expiredAttempt = user.CompletePasswordReset("token_hash_1", "new_hash", now.AddMinutes(31));
        Assert.True(expiredAttempt.IsFailure);
        Assert.Equal("PasswordReset.TokenExpired", expiredAttempt.Error.Code);

        // Emite novo token e conclui dentro da validade
        user.IssuePasswordResetToken("token_hash_2", now.AddMinutes(32), TimeSpan.FromMinutes(30), "127.0.0.1");
        var resetOk = user.CompletePasswordReset("token_hash_2", "new_hash", now.AddMinutes(40));
        Assert.True(resetOk.IsSuccess);
        Assert.False(session.IsActive(now.AddMinutes(41)));

        // Reutilização do mesmo token deve ser rejeitada
        var reuseAttempt = user.CompletePasswordReset("token_hash_2", "another_hash", now.AddMinutes(42));
        Assert.True(reuseAttempt.IsFailure);
        Assert.Equal("PasswordReset.TokenAlreadyUsed", reuseAttempt.Error.Code);
    }
}
