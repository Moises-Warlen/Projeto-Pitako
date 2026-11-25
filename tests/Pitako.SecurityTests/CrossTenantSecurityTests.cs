using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class CrossTenantSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateDb(c => c.SetTenant(_tenantA, TenantResolutionSource.AuthenticatedSession));
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private TenantsDbContext CreateDb(Action<ITenantContextSetter>? configure = null)
    {
        var context = new TenantContext();
        configure?.Invoke(context);
        var options = new DbContextOptionsBuilder<TenantsDbContext>().UseSqlite(_connection).Options;
        return new TenantsDbContext(options, context, new SystemClock());
    }

    [Fact]
    public async Task SaveChanges_ShouldBlockInsertUpdateAndDelete_AcrossTenants()
    {
        var unitOfB = StoreUnit.Create(_tenantB, "Unidade Secreta B", "SEC-B").Value;

        await using (var dbB = CreateDb(c => c.SetTenant(_tenantB, TenantResolutionSource.AuthenticatedSession)))
        {
            dbB.StoreUnits.Add(unitOfB);
            await dbB.SaveChangesAsync();
        }

        // 1. Tentativa da Loja A inserir entidade com TenantId da Loja B
        await using (var dbAInsert = CreateDb(c => c.SetTenant(_tenantA, TenantResolutionSource.AuthenticatedSession)))
        {
            var forgedUnit = StoreUnit.Create(_tenantB, "Forjada", "FORJ").Value;
            dbAInsert.StoreUnits.Add(forgedUnit);
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => dbAInsert.SaveChangesAsync());
        }

        // 2. Tentativa da Loja A alterar entidade pertencente à Loja B
        await using (var dbAModify = CreateDb(c => c.SetTenant(_tenantA, TenantResolutionSource.AuthenticatedSession)))
        {
            unitOfB.Rename("Hackeada");
            dbAModify.Attach(unitOfB);
            dbAModify.Entry(unitOfB).State = EntityState.Modified;
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => dbAModify.SaveChangesAsync());
        }

        // 3. Tentativa da Loja A excluir entidade pertencente à Loja B
        await using (var dbADelete = CreateDb(c => c.SetTenant(_tenantA, TenantResolutionSource.AuthenticatedSession)))
        {
            dbADelete.Attach(unitOfB);
            dbADelete.StoreUnits.Remove(unitOfB);
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => dbADelete.SaveChangesAsync());
        }

        // 4. Tentativa de gravação sem contexto de tenant resolvido
        await using (var unresolvedDb = CreateDb())
        {
            unresolvedDb.StoreUnits.Add(StoreUnit.Create(_tenantA, "Sem Contexto", "NOCTX").Value);
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => unresolvedDb.SaveChangesAsync());
        }
    }
}
