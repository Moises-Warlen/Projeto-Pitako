using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.UnitTests;

public sealed class TenantContextAndGuardTests
{
    [Fact]
    public void TenantContext_ShouldStartUnresolved_AndBlockCrossTenantOverwrite()
    {
        var context = new TenantContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Assert.False(((ITenantContext)context).IsResolved);
        Assert.Throws<CrossTenantAccessException>(() => TenantGuard.EnsureResolved(context));

        context.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);

        Assert.True(((ITenantContext)context).IsResolved);
        Assert.Equal(tenantA, context.TenantId);
        Assert.Throws<CrossTenantAccessException>(() =>
            context.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession));
        Assert.Throws<CrossTenantAccessException>(() => context.SetMasterScope());
    }

    [Fact]
    public void TenantGuard_ShouldRejectSpoofedClientTenantId()
    {
        var context = new TenantContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        context.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);

        TenantGuard.EnsureOwnership(context, tenantA);
        TenantGuard.RejectClientSuppliedMismatch(context, tenantA);
        TenantGuard.RejectClientSuppliedMismatch(context, null);

        Assert.Throws<CrossTenantAccessException>(() => TenantGuard.EnsureOwnership(context, tenantB));
        Assert.Throws<CrossTenantAccessException>(() => TenantGuard.RejectClientSuppliedMismatch(context, tenantB));
    }
}
