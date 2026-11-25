using Pitako.Modules.DiningHall.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class SelfServiceQrAndTotemDomainTests
{
    [Fact]
    public void QrTableSession_And_WaiterCall_Lifecycle_ShouldEnforceStateAndExpiry()
    {
        var tenantId = Guid.NewGuid();
        var waiterId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 19, 0, 0, TimeSpan.Zero);

        var sector = DiningSector.Create(tenantId, "Salão").Value;
        var table = DiningTable.Create(tenantId, sector.Id, "M10", 4).Value;
        var tab = DiningTab.OpenSelfService(tenantId, "COM-0010", table, "João QR", 1, 10m, utc0).Value;

        Assert.Equal(DiningTabOrigin.QrCodeTable, tab.Origin);
        Assert.Equal(DiningTableStatus.Occupied, table.Status);

        var session = QrTableSession.Create(tenantId, table, tab.Id, "João QR", utc0, ttlMinutes: 60).Value;
        Assert.True(session.EnsureOperational(utc0.AddMinutes(30)).IsSuccess);

        // Pedido de conta via QR altera status da comanda e mesa
        Assert.True(tab.RequestClosing(null, table, utc0.AddMinutes(40)).IsSuccess);
        Assert.Equal(DiningTabStatus.ClosingRequested, tab.Status);
        Assert.Equal(DiningTableStatus.AwaitingPayment, table.Status);
        Assert.True(session.RequestBill(utc0.AddMinutes(40)).IsSuccess);

        var call = WaiterCall.Create(tenantId, table, WaiterCallType.RequestBill, tab.Id, session.Id, "Maquininha", utc0.AddMinutes(40)).Value;
        Assert.Equal(WaiterCallStatus.Pending, call.Status);
        Assert.True(call.Acknowledge(waiterId, utc0.AddMinutes(41)).IsSuccess);
        Assert.Equal(WaiterCallStatus.Acknowledged, call.Status);

        // Não pode assumir novamente chamado já em atendimento
        Assert.True(call.Acknowledge(waiterId, utc0.AddMinutes(42)).IsFailure);
        Assert.True(call.Complete(waiterId, utc0.AddMinutes(45)).IsSuccess);
        Assert.Equal(WaiterCallStatus.Completed, call.Status);

        // Após TTL, sessão QR expira
        var expiredCheck = session.EnsureOperational(utc0.AddMinutes(65));
        Assert.True(expiredCheck.IsFailure);
        Assert.Equal("QrSession.Expired", expiredCheck.Error.Code);
    }

    [Fact]
    public void TotemOrderSession_ShouldValidateTimeout_PreserveCustomLines_AndConfirmIdempotently()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero);

        var terminal = TotemTerminal.Create(
            tenantId,
            "TOTEM-01",
            "Totem Entrada",
            TotemFulfillmentChoice.CounterPickup,
            requireCustomerName: true,
            requireTableOrPagerNumber: false,
            idleTimeoutSeconds: 60).Value;

        // Exige nome quando configurado no totem
        var missingName = TotemOrderSession.Start(tenantId, terminal, TotemFulfillmentChoice.DineIn, null, null, utc0);
        Assert.True(missingName.IsFailure);
        Assert.Equal("Totem.CustomerNameRequired", missingName.Error.Code);

        var session = TotemOrderSession.Start(tenantId, terminal, TotemFulfillmentChoice.DineIn, "Mariana", "Mesa 05", utc0).Value;

        // Carrinho vazio não confirma
        var emptyConfirm = session.Confirm(TotemPaymentChoice.PixOnTotem, "T-0001", "#0100", "k1", utc0);
        Assert.True(emptyConfirm.IsFailure);
        Assert.Equal("Totem.EmptyBasket", emptyConfirm.Error.Code);

        var prodId = Guid.NewGuid();
        var price = Money.Create(22.50m).Value;
        Assert.True(session.AddLine(prodId, null, "X-Salada", "Padrão", "{\"v\":1}", price, 1, null, terminal.IdleTimeoutSeconds, utc0.AddSeconds(5)).IsSuccess);
        // Mesmo item com mesma customização e observação soma quantidade
        Assert.True(session.AddLine(prodId, null, "X-Salada", "Padrão", "{\"v\":1}", price, 2, null, terminal.IdleTimeoutSeconds, utc0.AddSeconds(10)).IsSuccess);
        // Mesmo produto com observação diferente mantém linha separada
        Assert.True(session.AddLine(prodId, null, "X-Salada", "Padrão", "{\"v\":1}", price, 1, "Sem cebola", terminal.IdleTimeoutSeconds, utc0.AddSeconds(15)).IsSuccess);

        Assert.Equal(2, session.Lines.Count);
        Assert.Equal(90.00m, session.ItemsSubtotal.Amount);

        Assert.True(session.Confirm(TotemPaymentChoice.CardTapOnTotem, "T-0001", "#0100", "idem-1", utc0.AddSeconds(20)).IsSuccess);
        Assert.Equal(TotemSessionStatus.Confirmed, session.Status);
        Assert.Equal("T-0001", session.PickupCallCode);

        // Expiração por inatividade em nova sessão
        var idleSession = TotemOrderSession.Start(tenantId, terminal, TotemFulfillmentChoice.CounterPickup, "Carlos", null, utc0).Value;
        var expiredAdd = idleSession.AddLine(prodId, null, "X-Salada", "Padrão", "{}", price, 1, null, terminal.IdleTimeoutSeconds, utc0.AddSeconds(70));
        Assert.True(expiredAdd.IsFailure);
        Assert.Equal("Totem.SessionExpired", expiredAdd.Error.Code);
    }
}
