using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class InventoryStockProductionIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 13, 0, 0, TimeSpan.Zero));

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
    public async Task Purchases_Production_OrderDeduction_CancellationReversal_AndAlerts_ShouldWorkEndToEnd()
    {
        var tenantId = Guid.NewGuid();
        var stockist = User.CreateEstablishmentUser(tenantId, "Estoquista", "est@loja.com", "hash", EstablishmentRole.Stockist).Value;
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid polpaId;
        Guid copoId;
        Guid baseAcaiId;
        Guid productCopo500Id = Guid.NewGuid();
        Guid prepSheetId;

        await using (var db = CreateDb(ctx))
        {
            var supplySvc = new SuppliesAndTechnicalSheetService(
                new EfTenantCustomUnitRepository(db),
                new EfSupplyItemRepository(db),
                new EfTechnicalSheetRepository(db),
                ctx,
                _clock);

            var stockSvc = new InventoryStockAndProductionService(
                new EfTenantCustomUnitRepository(db),
                new EfSupplyItemRepository(db),
                new EfTechnicalSheetRepository(db),
                new EfStockBalanceRepository(db),
                new EfPurchaseReceiptRepository(db),
                new EfProductionOrderRepository(db),
                ctx,
                _clock);

            // Insumos: Polpa (1 caixa = 10 kg @ R$150), Copo (1 pacote = 100 un @ R$80), Base Açaí Pronta (kg @ R$20)
            var polpa = (await supplySvc.CreateSupplyItemAsync(admin, "Polpa Açaí", 150.00m, 1m, "caixa", "Fábrica Norte", null, 10m, "kg")).Value;
            var copo = (await supplySvc.CreateSupplyItemAsync(admin, "Copo 500ml", 80.00m, 1m, "pacote", "EmbalaTudo", null, 100m, "unidade")).Value;
            var baseAcai = (await supplySvc.CreateSupplyItemAsync(admin, "Base Açaí Pronta", 20.00m, 1m, "kg", "Produção Interna")).Value;
            polpaId = polpa.Id;
            copoId = copo.Id;
            baseAcaiId = baseAcai.Id;

            // Configurar saldos de estoque: polpa em g (min 5000g), copo em unidade (min 20, sem baixa automática p/ testar flag!), base em kg
            await stockSvc.ConfigureStockBalanceAsync(stockist, polpaId, "g", minimumStockQuantity: 5000m, allowNegativeStock: false, autoDeductOnOrder: true);
            await stockSvc.ConfigureStockBalanceAsync(stockist, copoId, "unidade", minimumStockQuantity: 20m, allowNegativeStock: false, autoDeductOnOrder: false);
            await stockSvc.ConfigureStockBalanceAsync(stockist, baseAcaiId, "kg", minimumStockQuantity: 2m, allowNegativeStock: false, autoDeductOnOrder: true);

            // Ficha de produção interna: 5 kg de polpa rendem 5 kg de Base Açaí Pronta
            var prepSheet = (await supplySvc.CreateTechnicalSheetAsync(
                admin,
                "Produção Base Açaí",
                yieldQuantity: 5m,
                yieldUnitCode: "kg",
                ingredients: [new IngredientInput(polpaId, 5000m, "g")],
                packagings: [])).Value;
            prepSheetId = prepSheet.Id;

            // Ficha do produto vendido: Copo 500ml consome 400g de Base Açaí + 1 Copo (Copo tem AutoDeductOnOrder = false!)
            await supplySvc.CreateTechnicalSheetAsync(
                admin,
                "Copo Açaí 500ml",
                yieldQuantity: 1m,
                yieldUnitCode: "unidade",
                ingredients: [new IngredientInput(baseAcaiId, 400m, "g")],
                packagings: [new PackagingInput(copoId, "Copo 500ml", 1m, "unidade")],
                productId: productCopo500Id);

            // 1. Entrada de compra com reajuste de preço da caixa para R$ 160,00 (2 caixas = 20000g) e 1 pacote de copo (100 un)
            var purchaseRes = await stockSvc.ReceivePurchaseAsync(
                stockist,
                "Fábrica Norte",
                "NF-9981",
                [
                    new PurchaseLineInput(polpaId, 2m, "caixa", 160.00m, "POLPA-L1", null, _clock.UtcNow.AddDays(3)),
                    new PurchaseLineInput(copoId, 1m, "pacote", 80.00m)
                ],
                updateSupplyPrice: true,
                idempotencyKey: "compra-nf-9981");

            Assert.True(purchaseRes.IsSuccess);
            Assert.Equal(400.00m, purchaseRes.Value.TotalAmount.Amount);

            // Idempotência da compra: reenvio retorna a mesma nota sem duplicar saldo
            var replayPurchase = await stockSvc.ReceivePurchaseAsync(
                stockist,
                "Fábrica Norte",
                "NF-9981",
                [new PurchaseLineInput(polpaId, 2m, "caixa", 160.00m)],
                idempotencyKey: "compra-nf-9981");
            Assert.Equal(purchaseRes.Value.Id, replayPurchase.Value.Id);

            var polpaBalance = await db.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == polpaId);
            Assert.Equal(20000m, polpaBalance.QuantityOnHand);

            // Preço de compra atualizado recalculou automaticamente a ficha de produção de R$ 75 para R$ 80 (5kg * R$16/kg)
            var updatedPrep = await db.TechnicalSheets.AsNoTracking().FirstAsync(t => t.Id == prepSheetId);
            Assert.Equal(80.0000m, updatedPrep.TotalCost);
            Assert.Equal(2, updatedPrep.Version);
        }

        await using (var db2 = CreateDb(ctx))
        {
            var stockSvc2 = new InventoryStockAndProductionService(
                new EfTenantCustomUnitRepository(db2),
                new EfSupplyItemRepository(db2),
                new EfTechnicalSheetRepository(db2),
                new EfStockBalanceRepository(db2),
                new EfPurchaseReceiptRepository(db2),
                new EfProductionOrderRepository(db2),
                ctx,
                _clock);

            // 2. Executa produção de 2 lotes (= 10 kg Base Açaí, consome 10000 g de Polpa -> restam 10000 g)
            var prodRes = await stockSvc2.ExecuteProductionOrderAsync(
                stockist,
                prepSheetId,
                batchMultiplier: 2m,
                outputSupplyItemId: baseAcaiId,
                outputBatchNumber: "BASE-01",
                outputExpiresAtUtc: _clock.UtcNow.AddDays(5));

            Assert.True(prodRes.IsSuccess);
            Assert.Equal(10m, prodRes.Value.ProducedQuantity);
            Assert.Equal(160.0000m, prodRes.Value.TotalProductionCost);

            var baseBal = await db2.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == baseAcaiId);
            var polpaBal = await db2.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == polpaId);
            Assert.Equal(10m, baseBal.QuantityOnHand);
            Assert.Equal(10000m, polpaBal.QuantityOnHand);

            // 3. Venda de 5 copos (cada um consome 400g = 0.4 kg de Base -> baixa 2.0 kg, restam 8 kg; copo tem AutoDeductOnOrder=false logo é pulado)
            var orderId = Guid.NewGuid();
            var deductRes = await stockSvc2.DeductForOrderSaleAsync(
                admin,
                orderId,
                "PED-1001",
                [new OrderStockLineInput(productCopo500Id, 5m, "unidade")]);

            Assert.True(deductRes.IsSuccess);
            Assert.Equal(1, deductRes.Value.MovementsCreated);
            Assert.Equal(1, deductRes.Value.SkippedItemsCount);

            var baseAfterSale = await db2.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == baseAcaiId);
            Assert.Equal(8.0m, baseAfterSale.QuantityOnHand);

            // Retry idempotente do mesmo pedido não baixa de novo
            var deductReplay = await stockSvc2.DeductForOrderSaleAsync(admin, orderId, "PED-1001", [new OrderStockLineInput(productCopo500Id, 5m, "unidade")]);
            Assert.Equal(0, deductReplay.Value.MovementsCreated);

            // 4. Estorno do pedido cancelado devolve exatamente os 2.0 kg
            var reversal = await stockSvc2.ReverseOrderDeductionAsync(admin, orderId, "Cancelamento cliente");
            Assert.True(reversal.IsSuccess);
            Assert.Equal(1, reversal.Value);

            var baseAfterCancel = await db2.StockBalances.AsNoTracking().FirstAsync(b => b.SupplyItemId == baseAcaiId);
            Assert.Equal(10.0m, baseAfterCancel.QuantityOnHand);

            // 5. Baixa de perda/ajuste deixando polpa abaixo do mínimo (10000 - 6000 = 4000 <= 5000) e verificando alertas
            await stockSvc2.RegisterWasteOrLossAsync(stockist, polpaId, 6000m, "Quebra de câmara fria");
            var alerts = (await stockSvc2.GetActiveAlertsAsync(stockist, expiringWithinDays: 7)).Value;

            Assert.Contains(alerts.LowStockAlerts, a => a.SupplyItemId == polpaId && a.QuantityOnHand == 4000m);
            Assert.Equal(2, alerts.ExpiringSoonLots.Count); // POLPA-L1 (3 dias) e BASE-01 (5 dias)
        }
    }
}
