using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class InventoryIsolationSecurityTests : IAsyncLifetime
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

    private InventoryDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadOrUse_SuppliesAndTechnicalSheetsOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var cashierA = User.CreateEstablishmentUser(tenantA, "Caixa A", "cx@a.com", "hash", EstablishmentRole.Cashier).Value;

        Guid supplyBId;
        Guid sheetBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var supplyB = SupplyItem.Create(tenantB, "Insumo Secreto B", Money.Create(30m).Value, 1m, "kg", createdAtUtc: _clock.UtcNow).Value;
            supplyBId = supplyB.Id;
            await dbB.SupplyItems.AddAsync(supplyB);

            var sheetB = TechnicalSheet.Create(
                tenantB,
                "Receita Secreta B",
                1m,
                "unidade",
                [new IngredientInput(supplyB.Id, 100m, "g")],
                [],
                new Dictionary<Guid, SupplyItem> { [supplyB.Id] = supplyB },
                null,
                _clock.UtcNow).Value;
            sheetBId = sheetB.Id;
            await dbB.TechnicalSheets.AddAsync(sheetB);
            await dbB.SaveChangesAsync();
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new SuppliesAndTechnicalSheetService(
                new EfTenantCustomUnitRepository(dbA),
                new EfSupplyItemRepository(dbA),
                new EfTechnicalSheetRepository(dbA),
                ctxA,
                _clock);

            // Global query filter oculta insumos e fichas do Tenant B
            Assert.Empty(await dbA.SupplyItems.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.TechnicalSheets.AsNoTracking().ToListAsync());

            // Tenant A não consegue atualizar preço do insumo de B
            var updateB = await svcA.UpdateSupplyPriceAsync(adminA, supplyBId, 1.00m, null, "Tentativa indevida");
            Assert.True(updateB.IsFailure);
            Assert.Equal("Supply.NotFound", updateB.Error.Code);

            // Tenant A não consegue criar ficha usando insumo do Tenant B
            var crossSheet = await svcA.CreateTechnicalSheetAsync(
                adminA,
                "Ficha Cruzada",
                1m,
                "unidade",
                [new IngredientInput(supplyBId, 100m, "g")],
                []);
            Assert.True(crossSheet.IsFailure);
            Assert.Equal("TechnicalSheet.SupplyNotFound", crossSheet.Error.Code);

            // Tenant A não consegue simular preço da ficha do Tenant B
            var simB = await svcA.SimulateSellingPriceAsync(adminA, sheetBId, 10m, 10m, 5m, 20m);
            Assert.True(simB.IsFailure);
            Assert.Equal("TechnicalSheet.NotFound", simB.Error.Code);

            // Menor privilégio: Caixa da própria loja A não pode cadastrar insumo
            var cashierAttempt = await svcA.CreateSupplyItemAsync(cashierA, "Insumo Proibido", 10m, 1m, "kg");
            Assert.True(cashierAttempt.IsFailure);
            Assert.Equal("Inventory.Forbidden", cashierAttempt.Error.Code);
        }
    }
}
