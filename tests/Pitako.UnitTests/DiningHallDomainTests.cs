using Pitako.Modules.DiningHall.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class DiningHallDomainTests
{
    [Fact]
    public void DiningTab_Transfer_CancelItem_ExactCentSplit_AndPaymentClosure_ShouldWorkCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var waiterId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero);

        var sector = DiningSector.Create(tenantId, "Salão Principal").Value;
        var table1 = DiningTable.Create(tenantId, sector.Id, "M01", 4).Value;
        var table2 = DiningTable.Create(tenantId, sector.Id, "M02", 6).Value;

        var tab = DiningTab.Open(tenantId, "COM-0001", waiterId, "Mesa Família", 3, 10m, table1, utc0).Value;
        Assert.Equal(DiningTableStatus.Occupied, table1.Status);

        // Segunda comanda não pode ocupar a mesma mesa M01
        var dupOccupy = DiningTab.Open(tenantId, "COM-0002", waiterId, "Outro", 1, 10m, table1, utc0);
        Assert.True(dupOccupy.IsFailure);
        Assert.Equal("Dining.TableAlreadyOccupied", dupOccupy.Error.Code);

        // Lança 2 itens: 2x R$ 35,00 (70) + 1x R$ 20,91 (20,91) -> Sub=90,91; +10% (9,09) = R$ 100,00
        var item1 = tab.AddItem(Guid.NewGuid(), "Prato Feito", "Padrão", Money.Create(35.00m).Value, 2, waiterId, utc0.AddMinutes(5)).Value;
        var item2 = tab.AddItem(Guid.NewGuid(), "Sobremesa Especial", "Sem calda", Money.Create(20.91m).Value, 1, waiterId, utc0.AddMinutes(6)).Value;
        var extraItem = tab.AddItem(Guid.NewGuid(), "Suco Laranja", "Com gelo", Money.Create(12.00m).Value, 1, waiterId, utc0.AddMinutes(7)).Value;

        // Cancela item extra -> volta exatamente para 90,91 + 9,09 = 100,00
        Assert.True(tab.CancelItem(extraItem.Id, "Cliente trocou de sabor", managerId, utc0.AddMinutes(8)).IsSuccess);
        Assert.Equal(90.91m, tab.ItemsSubtotal.Amount);
        Assert.Equal(9.09m, tab.ServiceChargeAmount.Amount);
        Assert.Equal(100.00m, tab.TotalAmount.Amount);

        // Transfere de M01 para M02 -> libera M01 e ocupa M02
        Assert.True(tab.TransferToTable(table1, table2, waiterId, utc0.AddMinutes(10)).IsSuccess);
        Assert.Equal(DiningTableStatus.Available, table1.Status);
        Assert.Equal(DiningTableStatus.Occupied, table2.Status);

        // Divisão exata de R$ 100,00 para 3 pagantes: 33,34 + 33,33 + 33,33 = 100,00
        var shares = tab.SplitRemainingEqually(3).Value;
        Assert.Equal(3, shares.Count);
        Assert.Equal(33.34m, shares[0].Amount.Amount);
        Assert.Equal(33.33m, shares[1].Amount.Amount);
        Assert.Equal(33.33m, shares[2].Amount.Amount);
        Assert.Equal(100.00m, shares.Sum(s => s.Amount.Amount));

        // Pagamento parcial muda status para ClosingRequested / AwaitingPayment; quitação fecha comanda e libera M02
        Assert.True(tab.RegisterPartialOrFullPayment(shares[0].Amount, waiterId, table2, utc0.AddMinutes(30)).IsSuccess);
        Assert.Equal(DiningTabStatus.ClosingRequested, tab.Status);
        Assert.Equal(DiningTableStatus.AwaitingPayment, table2.Status);

        Assert.True(tab.RegisterPartialOrFullPayment(Money.Create(66.66m).Value, waiterId, table2, utc0.AddMinutes(32)).IsSuccess);
        Assert.Equal(DiningTabStatus.Closed, tab.Status);
        Assert.Equal(DiningTableStatus.Available, table2.Status);
        Assert.NotNull(item1);
        Assert.NotNull(item2);
    }

    [Fact]
    public void TableReservation_ShouldValidateCapacityAndOverlap()
    {
        var tenantId = Guid.NewGuid();
        var sector = DiningSector.Create(tenantId, "Varanda").Value;
        var table = DiningTable.Create(tenantId, sector.Id, "V01", 4).Value;
        var slot = new DateTimeOffset(2026, 11, 26, 19, 0, 0, TimeSpan.Zero);

        var overCap = TableReservation.Create(tenantId, table, "Grupo Grande", "119", 6, slot);
        Assert.True(overCap.IsFailure);
        Assert.Equal("Reservation.ExceedsTableCapacity", overCap.Error.Code);

        var res1 = TableReservation.Create(tenantId, table, "Ana", "1199999", 4, slot, 90).Value;
        Assert.True(res1.OverlapsWith(slot.AddMinutes(45), 60));
        Assert.False(res1.OverlapsWith(slot.AddMinutes(90), 60));

        res1.Cancel();
        Assert.False(res1.OverlapsWith(slot, 90));
    }
}
