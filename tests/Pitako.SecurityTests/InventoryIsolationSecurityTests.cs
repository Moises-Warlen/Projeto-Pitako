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

    [Fact]
    public async Task TenantA_ShouldNeverReadModifyReceiveProduceOrDeduct_StockOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var stockistA = User.CreateEstablishmentUser(tenantA, "Estoquista A", "est@a.com", "hash", EstablishmentRole.Stockist).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid supplyBId;
        Guid sheetBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var supplySvcB = new SuppliesAndTechnicalSheetService(
                new EfTenantCustomUnitRepository(dbB),
                new EfSupplyItemRepository(dbB),
                new EfTechnicalSheetRepository(dbB),
                ctxB,
                _clock);

            var stockSvcB = new InventoryStockAndProductionService(
                new EfTenantCustomUnitRepository(dbB),
                new EfSupplyItemRepository(dbB),
                new EfTechnicalSheetRepository(dbB),
                new EfStockBalanceRepository(dbB),
                new EfPurchaseReceiptRepository(dbB),
                new EfProductionOrderRepository(dbB),
                ctxB,
                _clock);

            var supplyB = (await supplySvcB.CreateSupplyItemAsync(adminB, "Insumo B", 25m, 1m, "kg")).Value;
            supplyBId = supplyB.Id;

            await stockSvcB.ConfigureStockBalanceAsync(adminB, supplyBId, "kg", minimumStockQuantity: 100m);
            await stockSvcB.ReceivePurchaseAsync(adminB, "Fornecedor B", "NF-B1", [new PurchaseLineInput(supplyBId, 5m, "kg", 25m, "LOT-B1")]);

            var sheetB = (await supplySvcB.CreateTechnicalSheetAsync(
                adminB,
                "Receita B",
                1m,
                "unidade",
                [new IngredientInput(supplyBId, 1m, "kg")],
                [])).Value;
            sheetBId = sheetB.Id;

            await stockSvcB.ExecuteProductionOrderAsync(adminB, sheetBId, 1m);
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var stockSvcA = new InventoryStockAndProductionService(
                new EfTenantCustomUnitRepository(dbA),
                new EfSupplyItemRepository(dbA),
                new EfTechnicalSheetRepository(dbA),
                new EfStockBalanceRepository(dbA),
                new EfPurchaseReceiptRepository(dbA),
                new EfProductionOrderRepository(dbA),
                ctxA,
                _clock);

            // Global query filter oculta completamente saldos, lotes, movimentações, compras e produções de B
            Assert.Empty(await dbA.StockBalances.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.StockLots.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.StockMovements.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.PurchaseReceipts.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.ProductionOrders.AsNoTracking().ToListAsync());

            // Admin A não acessa nem altera estoque, compra ou produção de B
            var alertsA = await stockSvcA.GetActiveAlertsAsync(adminA);
            Assert.Empty(alertsA.Value.LowStockAlerts);

            var adjB = await stockSvcA.RegisterManualAdjustmentAsync(adminA, supplyBId, 10m, "Invasão");
            Assert.True(adjB.IsFailure);
            Assert.Equal("Stock.NotFound", adjB.Error.Code);

            var buyB = await stockSvcA.ReceivePurchaseAsync(adminA, "Fornecedor", null, [new PurchaseLineInput(supplyBId, 1m, "kg", 10m)]);
            Assert.True(buyB.IsFailure);
            Assert.Equal("Supply.NotFound", buyB.Error.Code);

            var prodB = await stockSvcA.ExecuteProductionOrderAsync(adminA, sheetBId, 1m);
            Assert.True(prodB.IsFailure);
            Assert.Equal("TechnicalSheet.NotFound", prodB.Error.Code);

            // Menor privilégio: Cozinha da própria loja A não pode registrar compra, enquanto Estoquista tem acesso
            var kitchenBuy = await stockSvcA.ReceivePurchaseAsync(kitchenA, "Forn", null, [new PurchaseLineInput(supplyBId, 1m, "kg", 10m)]);
            Assert.True(kitchenBuy.IsFailure);
            Assert.Equal("Inventory.Forbidden", kitchenBuy.Error.Code);

            var stockistAlertCheck = await stockSvcA.GetActiveAlertsAsync(stockistA);
            Assert.True(stockistAlertCheck.IsSuccess);
        }
    }
}
