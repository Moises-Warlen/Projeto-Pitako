using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Application.Services;
using Pitako.Modules.OfflineSync.Domain;
using Pitako.Modules.OfflineSync.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class TestOfflineOperationApplier : IOfflineOperationServerApplier
{
    public List<long> AppliedSequenceOrder { get; } = [];
    public HashSet<string> ConflictingKeys { get; } = ["Conflict-Entity"];

    public Task<OfflineApplyResult> ApplyAsync(Guid tenantId, OfflineSyncOperation operation, bool forceOnConflict, CancellationToken cancellationToken = default)
    {
        if (!forceOnConflict && ConflictingKeys.Contains(operation.EntityReferenceKey))
            return Task.FromResult(new OfflineApplyResult(OfflineApplyOutcomeKind.Conflict, null, "Caixa já encerrado no servidor."));

        AppliedSequenceOrder.Add(operation.ClientSequenceNumber);
        return Task.FromResult(new OfflineApplyResult(OfflineApplyOutcomeKind.Applied, Guid.NewGuid()));
    }
}

public sealed class OfflineSyncWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 59, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private OfflineSyncDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<OfflineSyncDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task OfflineSync_ShouldOrderBySequence_EnforceIdempotencyAndShaMismatch_AndResolveConflicts()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@pdv.com", "hash", EstablishmentRole.Manager).Value;
        var cashier = User.CreateEstablishmentUser(tenantId, "Caixa", "cx@pdv.com", "hash", EstablishmentRole.Cashier).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var applier = new TestOfflineOperationApplier();

        await using var db = CreateDb(ctx);
        var svc = new OfflineSynchronizationService(
            new EfOfflineTerminalRepository(db),
            new EfOfflineSyncOperationRepository(db),
            applier,
            ctx,
            _clock);

        var terminal = (await svc.RegisterTerminalAsync(manager, "OFF-PDV-01", "DEVICE-UUID-001", catalogSnapshotVersion: 5)).Value;

        // Lote chega fora de ordem (3, 1, 2) onde 3 gera conflito
        var batch = (await svc.SynchronizeBatchAsync(cashier, terminal.Id,
        [
            new OfflineOperationInput("k-3", 3, OfflineOperationType.CashMovement, "Conflict-Entity", "{\"v\":3}", 5, _clock.UtcNow.AddMinutes(-10)),
            new OfflineOperationInput("k-1", 1, OfflineOperationType.PdvSale, "Sale-1", "{\"total\":25.00}", 5, _clock.UtcNow.AddMinutes(-30)),
            new OfflineOperationInput("k-2", 2, OfflineOperationType.CounterOrder, "Order-2", "{\"total\":40.00}", 5, _clock.UtcNow.AddMinutes(-20))
        ])).Value;

        Assert.Equal([1L, 2L], applier.AppliedSequenceOrder);
        Assert.Equal(2, batch.SyncedCount);
        Assert.Equal(1, batch.ConflictCount);
        Assert.True(batch.HasPendingIssues);

        // Replay idempotente de k-1 com mesmo payload -> DuplicateIgnored (não re-executa)
        // Replay de k-1 com payload adulterado -> Conflict (OfflineSync.IdempotencyPayloadMismatch)
        var replay = (await svc.SynchronizeBatchAsync(cashier, terminal.Id,
        [
            new OfflineOperationInput("k-1", 1, OfflineOperationType.PdvSale, "Sale-1", "{\"total\":25.00}", 5, _clock.UtcNow.AddMinutes(-30)),
            new OfflineOperationInput("k-1", 1, OfflineOperationType.PdvSale, "Sale-1", "{\"total\":999.00}", 5, _clock.UtcNow.AddMinutes(-30))
        ])).Value;

        Assert.Equal(1, replay.DuplicateIgnoredCount);
        Assert.Equal(1, replay.ConflictCount);
        Assert.Equal([1L, 2L], applier.AppliedSequenceOrder);

        // Gerente lista conflito pendente e força aplicação auditada
        var conflicts = (await svc.ListPendingConflictsAsync(cashier)).Value;
        Assert.Single(conflicts);

        var resolved = (await svc.ResolveSyncConflictAsync(manager, conflicts[0].Id, OfflineConflictResolutionAction.ForceApplyClientWithAudit, "Autorizado lançamento retroativo")).Value;
        Assert.Equal(OfflineSyncStatus.ResolvedForcedApply, resolved.SyncStatus);
        Assert.Empty((await svc.ListPendingConflictsAsync(cashier)).Value);
    }
}
