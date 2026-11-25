using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Domain;
using Pitako.Modules.Weighing.Infrastructure.Hardware;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class WeighingIsolationSecurityTests : IAsyncLifetime
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

    private WeighingDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<WeighingDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadOrUse_ScalesTaresAndRecordsOfTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = User.CreateEstablishmentUser(tenantA, "Admin A", "a@a.com", "hash", EstablishmentRole.Administrator).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        Guid scaleBId;
        Guid tareBId;
        Guid recordBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new WeighingService(
                new EfScaleDeviceRepository(dbB),
                new EfTarePresetRepository(dbB),
                new EfWeighingRecordRepository(dbB),
                new UnavailablePhysicalScaleTransport(),
                ctxB,
                _clock);

            scaleBId = (await svcB.RegisterScaleDeviceAsync(adminB, "Balança B", ScaleManufacturer.Toledo, ScaleConnectionType.SerialPort, "COM1")).Value.Id;
            tareBId = (await svcB.RegisterTarePresetAsync(adminB, "Prato B", 300m)).Value.Id;
            recordBId = (await svcB.RegisterManualWeighingAsync(adminB, "Self Service B", 55m, 800m, "Teste B", scaleBId, null, tareBId)).Value.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using (var dbA = CreateDb(ctxA))
        {
            var svcA = new WeighingService(
                new EfScaleDeviceRepository(dbA),
                new EfTarePresetRepository(dbA),
                new EfWeighingRecordRepository(dbA),
                new UnavailablePhysicalScaleTransport(),
                ctxA,
                _clock);

            // Global query filter oculta balanças, taras e registros da Loja B
            Assert.Empty(await dbA.ScaleDevices.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.TarePresets.AsNoTracking().ToListAsync());
            Assert.Empty(await dbA.WeighingRecords.AsNoTracking().ToListAsync());

            // Admin A não consegue usar balança ou tara de B nem vincular pesagem de B a pedido de A
            var crossTare = await svcA.RegisterManualWeighingAsync(adminA, "Item A", 50m, 500m, "Tentativa cruzada", null, null, tareBId);
            Assert.True(crossTare.IsFailure);
            Assert.Equal("Tare.NotFound", crossTare.Error.Code);

            var crossScale = await svcA.CaptureAutomaticWeighingAsync(adminA, scaleBId, "Item A", 50m);
            Assert.True(crossScale.IsFailure);
            Assert.Equal("Scale.NotFound", crossScale.Error.Code);

            var crossAttach = await svcA.AttachToOrderAsync(adminA, recordBId, Guid.NewGuid());
            Assert.True(crossAttach.IsFailure);
            Assert.Equal("Weighing.NotFound", crossAttach.Error.Code);

            // Menor privilégio: Cozinha não pode configurar balança nem lançar pesagem
            var kitchenAttempt = await svcA.RegisterScaleDeviceAsync(kitchenA, "Balança Proibida", ScaleManufacturer.Toledo, ScaleConnectionType.ManualOnly, "MANUAL");
            Assert.True(kitchenAttempt.IsFailure);
            Assert.Equal("Weighing.Forbidden", kitchenAttempt.Error.Code);
        }
    }
}
