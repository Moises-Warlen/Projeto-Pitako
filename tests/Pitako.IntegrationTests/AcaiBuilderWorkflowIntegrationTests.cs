using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class AcaiBuilderWorkflowIntegrationTests : IAsyncLifetime
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

    private AcaiBuilderDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<AcaiBuilderDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task AcaiBuilder_FullFlow_ShouldConfigureCupSizesFreeAndPaidAreas_PauseAndPriceAccurately()
    {
        var tenantId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@acai.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid cup300Id;
        Guid cup500Id;
        Guid flavorZeroId;
        Guid leiteId;
        Guid granolaId;
        Guid morangoId;
        Guid nutellaId;

        await using (var db = CreateDb(ctx))
        {
            var svc = new AcaiBuilderManagementService(
                new EfAcaiCupSizeRepository(db),
                new EfAcaiBaseFlavorRepository(db),
                new EfAcaiFreeToppingRepository(db),
                new EfAcaiPaidAddonRepository(db),
                ctx);

            cup300Id = (await svc.CreateCupSizeAsync(admin, "300 ml", 300, 15.00m, freeToppingsLimit: 3, displayOrder: 1)).Value.Id;
            cup500Id = (await svc.CreateCupSizeAsync(admin, "500 ml", 500, 22.00m, freeToppingsLimit: 5, displayOrder: 2)).Value.Id;
            await svc.CreateCupSizeAsync(admin, "700 ml", 700, 28.00m, freeToppingsLimit: 6, displayOrder: 3);

            await svc.CreateBaseFlavorAsync(admin, "Açaí Tradicional", 0m, 1);
            flavorZeroId = (await svc.CreateBaseFlavorAsync(admin, "Açaí Zero Açúcar", 3.00m, 2)).Value.Id;

            leiteId = (await svc.CreateFreeToppingAsync(admin, "Leite em pó", 2, 1)).Value.Id;
            granolaId = (await svc.CreateFreeToppingAsync(admin, "Granola", 1, 2)).Value.Id;
            morangoId = (await svc.CreateFreeToppingAsync(admin, "Morango", 1, 3)).Value.Id;

            nutellaId = (await svc.CreatePaidAddonAsync(admin, "Nutella Original", 6.50m, 2, 1)).Value.Id;
            await svc.CreatePaidAddonAsync(admin, "Creme de Pistache", 8.00m, 2, 2);

            // Pausa morango e confirma separação das áreas
            await svc.PauseFreeToppingAsync(admin, morangoId);
        }

        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new AcaiBuilderManagementService(
                new EfAcaiCupSizeRepository(db2),
                new EfAcaiBaseFlavorRepository(db2),
                new EfAcaiFreeToppingRepository(db2),
                new EfAcaiPaidAddonRepository(db2),
                ctx);

            var menu = await svc2.GetMenuCatalogAsync(includePaused: false);
            Assert.Equal(3, menu.CupSizes.Count);
            Assert.Equal(2, menu.BaseFlavors.Count);
            Assert.Equal(2, menu.FreeToppings.Count); // Morango pausado oculto
            Assert.Equal(2, menu.PaidAddons.Count);   // Área separada

            // Montar copo com item pausado é bloqueado
            var pausedAttempt = await svc2.BuildAndPriceCupAsync(new AcaiCupBuildRequest(
                cup300Id,
                [flavorZeroId],
                [new AcaiItemSelection(morangoId, 1)],
                []));
            Assert.True(pausedAttempt.IsFailure);
            Assert.Equal("Acai.ToppingUnavailable", pausedAttempt.Error.Code);

            // Montagem válida no copo 500 ml: base Zero (+3.00) + 2x Leite + 1x Granola (3 de 5 grátis) + 1x Nutella (+6.50) = 31.50
            var built = await svc2.BuildAndPriceCupAsync(new AcaiCupBuildRequest(
                cup500Id,
                [flavorZeroId],
                [new AcaiItemSelection(leiteId, 2), new AcaiItemSelection(granolaId, 1)],
                [new AcaiItemSelection(nutellaId, 1)],
                CupQuantity: 2,
                CustomerNotes: "Sem colher"));

            Assert.True(built.IsSuccess);
            Assert.Equal(3, built.Value.FreeToppingsUsedCount);
            Assert.Equal(2, built.Value.FreeToppingsRemainingCount);
            Assert.Equal(31.50m, built.Value.UnitFinalPrice.Amount);
            Assert.Equal(63.00m, built.Value.TotalPrice.Amount);
        }
    }
}
