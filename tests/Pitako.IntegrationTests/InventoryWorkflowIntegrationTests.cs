using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class InventoryWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 11, 0, 0, TimeSpan.Zero));

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
    public async Task SupplyPriceUpdate_ShouldCascadeRecalculateTechnicalSheets_AndPreserveCostSnapshots()
    {
        var tenantId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid farinhaId;
        Guid queijoId;
        Guid sheetId;

        await using (var db = CreateDb(ctx))
        {
            var svc = new SuppliesAndTechnicalSheetService(
                new EfTenantCustomUnitRepository(db),
                new EfSupplyItemRepository(db),
                new EfTechnicalSheetRepository(db),
                ctx,
                _clock);

            // Unidade customizada: saco de 25kg (= 25.000g)
            var unitRes = await svc.RegisterCustomUnitAsync(admin, "saco25", "Saco 25kg", UnitDimension.Mass, 25000m);
            Assert.True(unitRes.IsSuccess);

            // Farinha: R$ 100,00 por saco25 -> R$ 4,00/kg -> 500g = R$ 2,00
            var farinha = (await svc.CreateSupplyItemAsync(admin, "Farinha Tipo 00", 100.00m, 1m, "saco25", "Moinho Sul")).Value;
            // Mussarela: R$ 40,00/kg -> 300g = R$ 12,00
            var queijo = (await svc.CreateSupplyItemAsync(admin, "Mussarela", 40.00m, 1m, "kg", "Laticínio Serra")).Value;
            farinhaId = farinha.Id;
            queijoId = queijo.Id;

            var sheetRes = await svc.CreateTechnicalSheetAsync(
                admin,
                "Massa & Cobertura Pizza G (Rende 2 unidades)",
                yieldQuantity: 2m,
                yieldUnitCode: "unidade",
                ingredients:
                [
                    new IngredientInput(farinha.Id, 500m, "g"),
                    new IngredientInput(queijo.Id, 300m, "g")
                ],
                packagings:
                [
                    new PackagingInput(null, "Caixa Oitavada", 2m, "unidade", ManualTotalCost: 4.00m)
                ]);

            Assert.True(sheetRes.IsSuccess);
            sheetId = sheetRes.Value.Id;
            // 2.00 + 12.00 + 4.00 = 18.00 total / 2 = 9.00 por unidade
            Assert.Equal(18.0000m, sheetRes.Value.TotalCost);
            Assert.Equal(9.0000m, sheetRes.Value.CostPerPortion);
            Assert.Equal(1, sheetRes.Value.Version);
        }

        // Avança o relógio e reajusta a mussarela de R$ 40,00 para R$ 50,00/kg (300g = R$ 15,00; aumento de R$ 3,00 no total, +R$ 1,50 por unidade)
        _clock.UtcNow = _clock.UtcNow.AddHours(3);

        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new SuppliesAndTechnicalSheetService(
                new EfTenantCustomUnitRepository(db2),
                new EfSupplyItemRepository(db2),
                new EfTechnicalSheetRepository(db2),
                ctx,
                _clock);

            var updateRes = await svc2.UpdateSupplyPriceAsync(admin, queijoId, 50.00m, "Laticínio Serra", "Reajuste nota fiscal #442");
            Assert.True(updateRes.IsSuccess);
            Assert.Equal(1, updateRes.Value.RecalculatedTechnicalSheetsCount);
            Assert.Equal(2, updateRes.Value.SupplyItem.PriceHistory.Count);

            var reloaded = await db2.TechnicalSheets
                .Include(t => t.Ingredients)
                .Include(t => t.Packagings)
                .Include(t => t.CostHistory)
                .AsNoTracking()
                .FirstAsync(t => t.Id == sheetId);

            Assert.Equal(2, reloaded.Version);
            Assert.Equal(17.0000m, reloaded.IngredientsTotalCost);
            Assert.Equal(4.0000m, reloaded.PackagingsTotalCost);
            Assert.Equal(21.0000m, reloaded.TotalCost);
            Assert.Equal(10.5000m, reloaded.CostPerPortion);

            // Histórico preservado com v1 e v2
            Assert.Equal(2, reloaded.CostHistory.Count);
            var v1 = reloaded.CostHistory.Single(h => h.Version == 1);
            var v2 = reloaded.CostHistory.Single(h => h.Version == 2);
            Assert.Equal(9.0000m, v1.CostPerPortion);
            Assert.Equal(10.5000m, v2.CostPerPortion);

            // Simula preço de venda com o novo custo atualizado (10.50 / (1 - 0.30) = 15.00; break-even 10.50 / 0.80 = 13.125 -> 13.12 ToEven)
            var simRes = await svc2.SimulateSellingPriceAsync(admin, sheetId, 10m, 10m, 0m, 10m);
            Assert.True(simRes.IsSuccess);
            Assert.Equal(15.00m, simRes.Value.SuggestedSellingPrice.Amount);
            Assert.Equal(13.12m, simRes.Value.BreakEvenPrice.Amount);
            Assert.NotEqual(Guid.Empty, farinhaId);
        }
    }
}
