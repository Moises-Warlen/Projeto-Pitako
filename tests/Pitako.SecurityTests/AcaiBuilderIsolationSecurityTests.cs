using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class AcaiBuilderIsolationSecurityTests : IAsyncLifetime
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
    public async Task TenantA_ShouldNeverReadModifyOrMix_AcaiMenuOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid cupBId;
        Guid toppingBId;
        Guid addonBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new AcaiBuilderManagementService(
                new EfAcaiCupSizeRepository(dbB),
                new EfAcaiBaseFlavorRepository(dbB),
                new EfAcaiFreeToppingRepository(dbB),
                new EfAcaiPaidAddonRepository(dbB),
                ctxB);

            cupBId = (await svcB.CreateCupSizeAsync(adminB, "Copo B 500ml", 500, 20m, 5)).Value.Id;
            toppingBId = (await svcB.CreateFreeToppingAsync(adminB, "Topping Secreto B")).Value.Id;
            addonBId = (await svcB.CreatePaidAddonAsync(adminB, "Nutella B", 7m)).Value.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new AcaiBuilderManagementService(
                new EfAcaiCupSizeRepository(dbA),
                new EfAcaiBaseFlavorRepository(dbA),
                new EfAcaiFreeToppingRepository(dbA),
                new EfAcaiPaidAddonRepository(dbA),
                ctxA);

            // Global query filter oculta toda configuração da Loja B
            var menuA = await svcA.GetMenuCatalogAsync(includePaused: true);
            Assert.Empty(menuA.CupSizes);
            Assert.Empty(menuA.BaseFlavors);
            Assert.Empty(menuA.FreeToppings);
            Assert.Empty(menuA.PaidAddons);

            // Admin A não consegue pausar itens da Loja B
            var pauseTopB = await svcA.PauseFreeToppingAsync(adminA, toppingBId);
            Assert.True(pauseTopB.IsFailure);
            Assert.Equal("Acai.ToppingNotFound", pauseTopB.Error.Code);

            var pauseAddonB = await svcA.PausePaidAddonAsync(adminA, addonBId);
            Assert.True(pauseAddonB.IsFailure);
            Assert.Equal("Acai.AddonNotFound", pauseAddonB.Error.Code);

            // Loja A cria seu próprio copo, mas não consegue montar copo usando topping/addon da Loja B
            var cupAId = (await svcA.CreateCupSizeAsync(adminA, "Copo A 300ml", 300, 15m, 3)).Value.Id;
            var crossCupBuild = await svcA.BuildAndPriceCupAsync(new AcaiCupBuildRequest(
                cupAId,
                [],
                [new AcaiItemSelection(toppingBId, 1)],
                []));
            Assert.True(crossCupBuild.IsFailure);
            Assert.Equal("Acai.ToppingUnavailable", crossCupBuild.Error.Code);

            var buildCupB = await svcA.BuildAndPriceCupAsync(new AcaiCupBuildRequest(cupBId, [], [], []));
            Assert.True(buildCupB.IsFailure);
            Assert.Equal("Acai.CupSizeUnavailable", buildCupB.Error.Code);

            // Menor privilégio: perfil Cozinha não pode configurar tamanhos ou adicionais
            var kitchenAttempt = await svcA.CreateCupSizeAsync(kitchenA, "Copo Cozinha", 400, 18m, 4);
            Assert.True(kitchenAttempt.IsFailure);
            Assert.Equal("Acai.Forbidden", kitchenAttempt.Error.Code);
        }
    }
}
