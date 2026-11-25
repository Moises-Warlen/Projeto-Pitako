using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class NoHistoryChecker : IProductHistoryReferenceChecker
{
    public Task<bool> HasHistoricalReferencesAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class CatalogIsolationSecurityTests : IAsyncLifetime
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

    private CatalogDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadModifyPauseOrDelete_CatalogOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;

        Guid productBId;
        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var catB = Category.Create(tenantB, "Pizzas B").Value;
            var prodB = Product.Create(tenantB, catB.Id, "Pizza Secreta B", Money.Create(60m).Value).Value;
            productBId = prodB.Id;
            await dbB.Categories.AddAsync(catB);
            await dbB.Products.AddAsync(prodB);
            await dbB.SaveChangesAsync();
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new CatalogManagementService(
                new EfCategoryRepository(dbA),
                new EfProductRepository(dbA),
                new EfModifierGroupRepository(dbA),
                new EfComboRepository(dbA),
                new EfPromotionRepository(dbA),
                new NoHistoryChecker(),
                ctxA,
                _clock);

            // Global query filter oculta completamente catálogo da Loja B
            Assert.Empty(await dbA.Products.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.Categories.AsNoTracking().ToListAsync());

            var pauseAttempt = await svcA.PauseProductAsync(adminA, productBId);
            Assert.True(pauseAttempt.IsFailure);
            Assert.Equal("Product.NotFound", pauseAttempt.Error.Code);

            var deleteAttempt = await svcA.DeleteOrArchiveProductAsync(adminA, productBId);
            Assert.True(deleteAttempt.IsFailure);

            // Usuário Cozinha da própria Loja A não tem permissão para criar categoria (menor privilégio)
            var kitchenCreate = await svcA.CreateCategoryAsync(kitchenA, "Tentativa Cozinha");
            Assert.True(kitchenCreate.IsFailure);
            Assert.Equal("Catalog.Forbidden", kitchenCreate.Error.Code);
        }
    }
}
