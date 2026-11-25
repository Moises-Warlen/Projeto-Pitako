using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class DeliveryFreightIntegrationTests : IAsyncLifetime
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

    private DeliveryLogisticsDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<DeliveryLogisticsDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task DeliveryPolicy_TiersZonesHaversineAndFreeShipping_ShouldPersistAndQuoteAcrossContexts()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var admin = User.CreateEstablishmentUser(tenantId, "Admin", "admin@loja.com", "hash", EstablishmentRole.Administrator).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        await using (var db = CreateDb(ctx))
        {
            var svc = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(db), ctx);

            var policy = (await svc.CreatePolicyAsync(
                admin,
                originLatitude: -23.5613,
                originLongitude: -46.6565,
                maxRadiusKm: 8m,
                calculationMode: FreightCalculationMode.DistanceTiered,
                baseFeeAmount: 0m,
                includedKm: 0m,
                feePerKmAmount: 0m,
                baseEstimatedMinutes: 30,
                minimumOrderAmount: 30.00m,
                freeShippingThresholdAmount: 100.00m,
                storeUnitId: unitId)).Value;

            await svc.AddDistanceTierAsync(admin, policy.Id, 0m, 2m, 5.00m, 25);
            await svc.AddDistanceTierAsync(admin, policy.Id, 2m, 5m, 9.50m, 35);
            await svc.AddDistanceTierAsync(admin, policy.Id, 5m, 8m, 15.00m, 50);
            await svc.AddZoneRuleAsync(admin, policy.Id, "Zona Restrita", "08888", 0m, 60, isBlocked: true);
        }

        await using (var db2 = CreateDb(ctx))
        {
            var svc2 = new DeliveryPolicyAndFreightService(new EfDeliveryPolicyRepository(db2), ctx);

            // Paulista -> Ibirapuera (~2.90 km -> faixa 2-5 km = R$ 9,50)
            var quote = await svc2.QuoteFreightAsync(unitId, new DeliveryFreightQuoteRequest(
                Money.Create(60m).Value,
                DestinationLatitude: -23.5874,
                DestinationLongitude: -46.6576));

            Assert.True(quote.IsSuccess);
            Assert.Equal(9.50m, quote.Value.FinalDeliveryFee.Amount);
            Assert.Equal(35, quote.Value.EstimatedMinutes);

            // Com subtotal >= R$100 -> frete isento
            var freeQuote = await svc2.QuoteFreightAsync(unitId, new DeliveryFreightQuoteRequest(
                Money.Create(110m).Value,
                DestinationLatitude: -23.5874,
                DestinationLongitude: -46.6576));

            Assert.True(freeQuote.IsSuccess);
            Assert.True(freeQuote.Value.IsFreeShippingApplied);
            Assert.Equal(0.00m, freeQuote.Value.FinalDeliveryFee.Amount);

            // Zona bloqueada
            var blocked = await svc2.QuoteFreightAsync(unitId, new DeliveryFreightQuoteRequest(
                Money.Create(80m).Value,
                DestinationLatitude: -23.5620,
                DestinationLongitude: -46.6570,
                ZipCode: "08888-000"));
            Assert.True(blocked.IsFailure);
            Assert.Equal("Delivery.ZoneBlocked", blocked.Error.Code);
        }
    }
}
