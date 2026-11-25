using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Application.Services;
using Pitako.Modules.OfflineSync.Domain;
using Pitako.Modules.OfflineSync.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecurityAlwaysApplyOfflineApplier : IOfflineOperationServerApplier
{
    public Task<OfflineApplyResult> ApplyAsync(Guid tenantId, OfflineSyncOperation operation, bool forceOnConflict, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OfflineApplyResult(OfflineApplyOutcomeKind.Conflict, null, "Conflito simulado"));
}

public sealed class OfflineSyncIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();

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
    public async Task TenantA_ShouldNeverReadOrResolve_TenantB_OfflineOperations_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var cashierA = User.CreateEstablishmentUser(tenantA, "Caixa A", "cx@a.com", "hash", EstablishmentRole.Cashier).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var applier = new SecurityAlwaysApplyOfflineApplier();
        Guid terminalBId;
        Guid conflictBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new OfflineSynchronizationService(new EfOfflineTerminalRepository(dbB), new EfOfflineSyncOperationRepository(dbB), applier, ctxB, _clock);
            var termB = (await svcB.RegisterTerminalAsync(adminB, "OFF-B", "FP-B")).Value;
            terminalBId = termB.Id;
            await svcB.SynchronizeBatchAsync(adminB, terminalBId, [new OfflineOperationInput("k-b", 1, OfflineOperationType.PdvSale, "S-B", "{}", 1, _clock.UtcNow)]);
            conflictBId = (await svcB.ListPendingConflictsAsync(adminB)).Value[0].Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new OfflineSynchronizationService(new EfOfflineTerminalRepository(dbA), new EfOfflineSyncOperationRepository(dbA), applier, ctxA, _clock);

        // Global Query Filter oculta terminais e operações de B
        Assert.Empty(await dbA.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.SyncOperations.AsNoTracking().ToListAsync());

        // Cross-tenant sync e resolução falham
        var crossBatch = await svcA.SynchronizeBatchAsync(managerA, terminalBId, [new OfflineOperationInput("k-x", 1, OfflineOperationType.PdvSale, "X", "{}", 1, _clock.UtcNow)]);
        Assert.True(crossBatch.IsFailure);
        Assert.Equal("Offline.TerminalNotFound", crossBatch.Error.Code);

        var crossResolve = await svcA.ResolveSyncConflictAsync(managerA, conflictBId, OfflineConflictResolutionAction.AcceptServerAndDiscardClient, "Inv");
        Assert.True(crossResolve.IsFailure);
        Assert.Equal("Offline.OperationNotFound", crossResolve.Error.Code);

        // RBAC menor privilégio:
        // - Caixa NÃO pode cadastrar novo terminal offline nem aprovar resolução forçada (exige Approve -> Gerente/Admin)
        var cashierReg = await svcA.RegisterTerminalAsync(cashierA, "OFF-A", "FP-A");
        Assert.True(cashierReg.IsFailure);
        Assert.Equal("OfflineSync.Forbidden", cashierReg.Error.Code);

        // - Cozinha é bloqueada de qualquer operação offline de PDV
        var kitchenList = await svcA.ListPendingConflictsAsync(kitchenA);
        Assert.True(kitchenList.IsFailure);
        Assert.Equal("OfflineSync.Forbidden", kitchenList.Error.Code);
    }
}
