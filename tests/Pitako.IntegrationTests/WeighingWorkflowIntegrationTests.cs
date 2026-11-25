using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Domain;
using Pitako.Modules.Weighing.Infrastructure.Hardware;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class ProtocolFrameTestScaleTransport(string nextFrame) : IScaleTransport
{
    public string NextFrame { get; set; } = nextFrame;

    public Task<Result<string>> ReadRawFrameAsync(ScaleDeviceConfig scale, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(NextFrame));
}

public sealed class WeighingWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.Zero));

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
    public async Task Weighing_ToledoProtocolAutomatic_HardwareUnavailable_AndManualFallback_ShouldPersistAndAudit()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@loja.com", "hash", EstablishmentRole.Manager).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        Guid scaleId;
        Guid tareId;
        Guid autoRecordId;

        var frameTransport = new ProtocolFrameTestScaleTransport("\u000200522\u0003");

        await using (var db = CreateDb(ctx))
        {
            var svc = new WeighingService(
                new EfScaleDeviceRepository(db),
                new EfTarePresetRepository(db),
                new EfWeighingRecordRepository(db),
                frameTransport,
                ctx,
                _clock);

            var scale = (await svc.RegisterScaleDeviceAsync(
                manager,
                "Toledo Prix 3 Balcão",
                ScaleManufacturer.Toledo,
                ScaleConnectionType.SerialPort,
                "COM3",
                allowManualFallback: true,
                isDefault: true)).Value;
            scaleId = scale.Id;

            var tare = (await svc.RegisterTarePresetAsync(manager, "Copo 500ml", 22m)).Value;
            tareId = tare.Id;

            // Leitura automática via quadro Toledo: 522g bruto - 22g tara = 500g líquido @ R$ 60,00/kg = R$ 30,00
            var autoRes = await svc.CaptureAutomaticWeighingAsync(
                manager,
                scaleId,
                "Açaí no Quilo",
                60.00m,
                Guid.NewGuid(),
                tareId);

            Assert.True(autoRes.IsSuccess);
            autoRecordId = autoRes.Value.Id;
            Assert.Equal(WeighingCaptureMode.AutomaticScale, autoRes.Value.CaptureMode);
            Assert.Equal(500m, autoRes.Value.NetWeightGrams);
            Assert.Equal(30.00m, autoRes.Value.TotalPrice.Amount);
            Assert.Equal("00522", autoRes.Value.RawScalePayload);
        }

        // Sem hardware físico acoplado: transporte real retorna falha honesta e operador registra contingência manual auditada
        await using (var db2 = CreateDb(ctx))
        {
            var unavailableSvc = new WeighingService(
                new EfScaleDeviceRepository(db2),
                new EfTarePresetRepository(db2),
                new EfWeighingRecordRepository(db2),
                new UnavailablePhysicalScaleTransport(),
                ctx,
                _clock);

            var hardwareFail = await unavailableSvc.CaptureAutomaticWeighingAsync(manager, scaleId, "Açaí no Quilo", 60.00m);
            Assert.True(hardwareFail.IsFailure);
            Assert.Equal("Scale.PhysicalHardwareNotConnected", hardwareFail.Error.Code);

            var manualRes = await unavailableSvc.RegisterManualWeighingAsync(
                manager,
                "Açaí no Quilo",
                60.00m,
                grossWeightGrams: 422m,
                manualReason: "Cabo serial desconectado durante pico",
                scaleDeviceId: scaleId,
                tarePresetId: tareId);

            Assert.True(manualRes.IsSuccess);
            Assert.Equal(WeighingCaptureMode.ManualFallback, manualRes.Value.CaptureMode);
            Assert.Equal(400m, manualRes.Value.NetWeightGrams);
            Assert.Equal(24.00m, manualRes.Value.TotalPrice.Amount);
            Assert.Equal(manager.Id, manualRes.Value.OperatorUserId);

            var orderId = Guid.NewGuid();
            var attachRes = await unavailableSvc.AttachToOrderAsync(manager, autoRecordId, orderId);
            Assert.True(attachRes.IsSuccess);
            Assert.Equal(orderId, attachRes.Value.LinkedOrderId);
        }
    }
}
