using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class DiningHallIsolationSecurityTests : IAsyncLifetime
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

    private DiningHallDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DiningHallDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadMutateOrResolveQr_DiningHallOfTenantB_AndRbacLeastPrivilegeMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var waiterA = User.CreateEstablishmentUser(tenantA, "Garçom A", "gar@a.com", "hash", EstablishmentRole.Waiter).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid tableBId;
        Guid tabBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new DiningHallManagementService(new EfDiningSectorRepository(dbB), new EfDiningTableRepository(dbB), new EfTableReservationRepository(dbB), new EfDiningTabRepository(dbB), ctxB, _clock);
            var secB = (await svcB.CreateSectorAsync(adminB, "Salão B")).Value;
            var tb = (await svcB.CreateTableAsync(adminB, secB.Id, "B01", 4, "QR-SECRETO-B")).Value;
            tableBId = tb.Id;
            var tabB = (await svcB.OpenTabAsync(adminB, "Cliente B", 2, tableBId)).Value;
            tabBId = tabB.Id;
            await svcB.AddTabItemAsync(adminB, tabBId, Guid.NewGuid(), "Prato B", "Padrão", 50m, 1);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new DiningHallManagementService(new EfDiningSectorRepository(dbA), new EfDiningTableRepository(dbA), new EfTableReservationRepository(dbA), new EfDiningTabRepository(dbA), ctxA, _clock);

        // Global query filter oculta setores, mesas, reservas, comandas e itens da Loja B
        Assert.Empty(await dbA.Sectors.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.Tables.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.Tabs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.TabItems.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.TabAuditEvents.AsNoTracking().ToListAsync());

        // Admin A não resolve QR Code de mesa de B nem lança item em comanda de B
        var qrB = await svcA.ResolveTableByQrTokenAsync("QR-SECRETO-B");
        Assert.True(qrB.IsFailure);
        Assert.Equal("Dining.TableNotFound", qrB.Error.Code);

        var crossAdd = await svcA.AddTabItemAsync(adminA, tabBId, Guid.NewGuid(), "Invasão", "X", 10m, 1);
        Assert.True(crossAdd.IsFailure);
        Assert.Equal("Tab.NotFound", crossAdd.Error.Code);

        // Na própria Loja A: Garçom abre comanda e lança item, mas NÃO pode cancelar item lançado (exige gerente/Cancel)
        var secA = (await svcA.CreateSectorAsync(adminA, "Salão A")).Value;
        var tA = (await svcA.CreateTableAsync(adminA, secA.Id, "A01", 4)).Value;
        var tabA = (await svcA.OpenTabAsync(waiterA, "Cli A", 2, tA.Id)).Value;
        var updatedTabA = (await svcA.AddTabItemAsync(waiterA, tabA.Id, Guid.NewGuid(), "Suco", "Natural", 12m, 1)).Value;

        var waiterCancel = await svcA.CancelTabItemAsync(waiterA, tabA.Id, updatedTabA.Items.First().Id, "Garçom tentando cancelar");
        Assert.True(waiterCancel.IsFailure);
        Assert.Equal("Dining.Forbidden", waiterCancel.Error.Code);

        // Cozinha não abre comanda nem altera mesas
        var kitchenOpen = await svcA.OpenTabAsync(kitchenA, "Cozinha", 1, tA.Id);
        Assert.True(kitchenOpen.IsFailure);
        Assert.Equal("Dining.Forbidden", kitchenOpen.Error.Code);
    }
}
