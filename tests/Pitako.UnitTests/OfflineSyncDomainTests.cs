using Pitako.Modules.OfflineSync.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class OfflineSyncDomainTests
{
    [Fact]
    public void OfflineSyncOperation_ShouldValidateOfflineWindowAndFutureClock_AndResolveConflictsWithAudit()
    {
        var tenantId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 11, 25, 22, 0, 0, TimeSpan.Zero);

        var terminal = OfflineTerminalRegistration.Register(tenantId, "OFF-01", "WIN-POS-FINGERPRINT", 3, maxOfflineHours: 24).Value;

        var expiredWindow = OfflineSyncOperation.Create(
            tenantId,
            terminal,
            "key-old",
            1,
            OfflineOperationType.PdvSale,
            "Venda-1",
            "{}",
            3,
            utcNow.AddHours(-30),
            utcNow);

        Assert.True(expiredWindow.IsFailure);
        Assert.Equal("Offline.MaxOfflineWindowExceeded", expiredWindow.Error.Code);

        var futureSkew = OfflineSyncOperation.Create(
            tenantId,
            terminal,
            "key-fut",
            2,
            OfflineOperationType.PdvSale,
            "Venda-2",
            "{}",
            3,
            utcNow.AddMinutes(10),
            utcNow);

        Assert.True(futureSkew.IsFailure);
        Assert.Equal("Offline.FutureTimestamp", futureSkew.Error.Code);

        var op = OfflineSyncOperation.Create(
            tenantId,
            terminal,
            "key-ok",
            3,
            OfflineOperationType.CashMovement,
            "Caixa-01",
            "{\"amount\":50}",
            3,
            utcNow.AddHours(-2),
            utcNow).Value;

        op.MarkConflict("Caixa já fechado no servidor", utcNow);
        Assert.Equal(OfflineSyncStatus.ConflictPendingResolution, op.SyncStatus);

        var resolved = op.ResolveConflict(
            OfflineConflictResolutionAction.AcceptServerAndDiscardClient,
            Guid.NewGuid(),
            "Conferido fechamento no caixa principal",
            null,
            utcNow.AddMinutes(5));

        Assert.True(resolved.IsSuccess);
        Assert.Equal(OfflineSyncStatus.ResolvedDiscardClient, op.SyncStatus);
    }
}
