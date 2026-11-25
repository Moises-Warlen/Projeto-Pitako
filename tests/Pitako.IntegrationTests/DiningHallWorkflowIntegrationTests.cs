using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class DiningHallWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 22, 0, 0, TimeSpan.Zero));

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
    public async Task DiningHall_Reservations_Tabs_Transfers_SplitAndSettlement_ShouldPersistCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var waiter = User.CreateEstablishmentUser(tenantId, "Garçom Lucas", "lucas@loja.com", "hash", EstablishmentRole.Waiter).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid table1Id;
        Guid table2Id;
        Guid tabId;
        Guid canceledItemId;
        string table2QrToken;

        await using (var db = CreateDb(ctx))
        {
            var svc = new DiningHallManagementService(
                new EfDiningSectorRepository(db),
                new EfDiningTableRepository(db),
                new EfTableReservationRepository(db),
                new EfDiningTabRepository(db),
                ctx,
                _clock);

            var sector = (await svc.CreateSectorAsync(manager, "Salão Nobre")).Value;
            var t1 = (await svc.CreateTableAsync(manager, sector.Id, "M01", 4)).Value;
            var t2 = (await svc.CreateTableAsync(manager, sector.Id, "M02", 6, "QR-MESA-02")).Value;
            table1Id = t1.Id;
            table2Id = t2.Id;
            table2QrToken = t2.QrCodeToken;

            var resSlot = _clock.UtcNow.AddDays(1);
            await svc.CreateReservationAsync(waiter, table2Id, "Família Costa", "1198888", 5, resSlot, 90);
            var conflict = await svc.CreateReservationAsync(waiter, table2Id, "Outro", "1197777", 2, resSlot.AddMinutes(30), 60);
            Assert.True(conflict.IsFailure);
            Assert.Equal("Reservation.TimeConflict", conflict.Error.Code);

            var tab = (await svc.OpenTabAsync(waiter, "Carlos & Amigos", 2, table1Id, serviceChargePercent: 10m)).Value;
            tabId = tab.Id;

            await svc.AddTabItemAsync(waiter, tabId, Guid.NewGuid(), "Pizza Grande", "Metade Calabresa", 60.00m, 1);
            var tabAfterExtra = (await svc.AddTabItemAsync(waiter, tabId, Guid.NewGuid(), "Refrigerante 2L", "Coca-Cola", 15.00m, 1)).Value;
            canceledItemId = tabAfterExtra.Items.First(i => i.ItemTitle == "Refrigerante 2L").Id;
        }

        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new DiningHallManagementService(
                new EfDiningSectorRepository(db2),
                new EfDiningTableRepository(db2),
                new EfTableReservationRepository(db2),
                new EfDiningTabRepository(db2),
                ctx,
                _clock);

            // Gerente cancela o item errado -> total fica 60 + 10% = 66,00
            await svc2.CancelTabItemAsync(manager, tabId, canceledItemId, "Garçom lançou duplicado");
            // Garçom transfere para M02
            await svc2.TransferTabToTableAsync(waiter, tabId, table2Id);

            var split = (await svc2.CalculateEqualSplitAsync(waiter, tabId, 2)).Value;
            Assert.Equal(33.00m, split[0].Amount.Amount);
            Assert.Equal(33.00m, split[1].Amount.Amount);

            await svc2.RegisterTabPaymentAsync(waiter, tabId, 33.00m);
            var closed = (await svc2.RegisterTabPaymentAsync(waiter, tabId, 33.00m)).Value;
            Assert.Equal(DiningTabStatus.Closed, closed.Status);

            var t1 = await db2.Tables.AsNoTracking().FirstAsync(t => t.Id == table1Id);
            var t2 = await db2.Tables.AsNoTracking().FirstAsync(t => t.Id == table2Id);
            Assert.Equal(DiningTableStatus.Available, t1.Status);
            Assert.Equal(DiningTableStatus.Available, t2.Status);

            var qrResolved = await svc2.ResolveTableByQrTokenAsync(table2QrToken);
            Assert.True(qrResolved.IsSuccess);
            Assert.Equal(table2Id, qrResolved.Value.Id);
        }
    }
}
