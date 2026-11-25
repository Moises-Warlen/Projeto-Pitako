using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class StoreBrandingIsolationSecurityTests : IAsyncLifetime
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

    private TenantsDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<TenantsDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task StoreAdminA_ShouldNeverViewEditOrPublish_BrandingOfStoreB()
    {
        var storeA = Tenant.Create("Loja A LTDA", "Loja A", "loja-a", "11222333000181", BusinessSegmentCatalog.Hamburgueria).Value;
        var storeB = Tenant.Create("Loja B LTDA", "Loja B", "loja-b", "06990590000123", BusinessSegmentCatalog.Pizzaria).Value;

        var adminA = User.CreateEstablishmentUser(storeA.Id, "Admin A", "admin@a.com", "hash", EstablishmentRole.Administrator).Value;
        var adminB = User.CreateEstablishmentUser(storeB.Id, "Admin B", "admin@b.com", "hash", EstablishmentRole.Administrator).Value;

        var ctxB = new TenantContext();
        ctxB.SetTenant(storeB.Id, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            dbB.Tenants.AddRange(storeA, storeB);
            var idB = StoreVisualIdentity.CreateDefault(storeB.Id);
            idB.UpdateDraftBranding("https://b.com/logo.png", null, "Pizza B", "#166534", "#FFFFFF", "#FACC15", "grid");
            idB.Publish(DateTimeOffset.UtcNow);
            await dbB.StoreVisualIdentities.AddAsync(idB);
            await dbB.SaveChangesAsync();
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(storeA.Id, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new StoreBrandingService(
                new EfTenantRepository(dbA),
                new EfStoreVisualIdentityRepository(dbA),
                ctxA,
                _clock);

            await Assert.ThrowsAsync<CrossTenantAccessException>(() => svcA.GetDraftPreviewAsync(adminA, storeB.Id));
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => svcA.UpdateDraftAsync(adminA, storeB.Id,
                new UpdateStoreBrandingDraftCommand("https://hack.com/logo.png", null, "Hack", "#000000", "#FFFFFF", "#111111", "grid")));
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => svcA.PublishBrandingAsync(adminA, storeB.Id));

            // Consulta direta ao DbContext no escopo da Loja A não enxerga identidade visual da Loja B
            var visibleIdentitiesToA = await dbA.StoreVisualIdentities.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(visibleIdentitiesToA, i => i.TenantId == storeB.Id);

            // Mesmo se Admin B tentar operar sob o contexto autenticado da Loja A, deve falhar
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => svcA.GetDraftPreviewAsync(adminB, storeA.Id));
        }
    }
}
