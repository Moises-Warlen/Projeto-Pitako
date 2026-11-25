using Pitako.Modules.Orders.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class OrderKanbanDomainTests
{
    [Fact]
    public void Order_CreateFromCart_ShouldPreserveSeparateItems_AndEnforceKanbanStateMachine()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero);
        var cart = ShoppingCart.Create(tenantId, "sess-1", utc0.AddDays(1), "cli@teste.com").Value;
        var cupId = Guid.NewGuid();

        cart.AddItem(CartItemKind.CustomAcaiCup, null, null, null, cupId, null, "Açaí 500 ml", "Copo 1", "{\"c\":1}", Money.Create(25m).Value, 1, false, "Copo A", utc0);
        cart.AddItem(CartItemKind.CustomAcaiCup, null, null, null, cupId, null, "Açaí 500 ml", "Copo 2", "{\"c\":2}", Money.Create(30m).Value, 1, false, "Copo B", utc0);

        // Sem endereço para Delivery deve falhar
        var noAddress = Order.CreateFromCart(
            cart, "#0001", OrderChannel.OwnDeliveryMenu, FulfillmentMode.Delivery,
            "Maria", "11999999999", null, Money.Create(8m).Value, Money.Zero(), Money.Zero(), null, null, null, null, utc0);
        Assert.True(noAddress.IsFailure);
        Assert.Equal("Order.AddressRequired", noAddress.Error.Code);

        // Pedido válido (Retirada balcão)
        var order = Order.CreateFromCart(
            cart, "#0001", OrderChannel.PdvCounter, FulfillmentMode.CounterPickup,
            "Maria", null, null, Money.Zero(), Money.Zero(), Money.Create(5m).Value, "CUPOM5", null, Guid.NewGuid(), "key-1", utc0).Value;

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(50.00m, order.TotalAmount.Amount);
        Assert.Equal(OrderStatus.Received, order.Status);

        var actor = Guid.NewGuid();
        Assert.True(order.TransitionTo(OrderStatus.Accepted, actor, "Aceito no balcão", utc0.AddMinutes(1)).IsSuccess);
        Assert.True(order.TransitionTo(OrderStatus.InPreparation, actor, "Na cozinha", utc0.AddMinutes(2)).IsSuccess);
        Assert.True(order.TransitionTo(OrderStatus.Ready, actor, "Pronto para retirada", utc0.AddMinutes(10)).IsSuccess);

        // Modalidade CounterPickup NÃO pode ir para Dispatched (Saiu para entrega)
        var invalidDispatch = order.TransitionTo(OrderStatus.Dispatched, actor, "Tentativa", utc0.AddMinutes(11));
        Assert.True(invalidDispatch.IsFailure);
        Assert.Equal("Order.InvalidKanbanTransition", invalidDispatch.Error.Code);

        // Conclui diretamente de Ready -> Completed
        Assert.True(order.TransitionTo(OrderStatus.Completed, actor, "Entregue ao cliente", utc0.AddMinutes(12)).IsSuccess);
        Assert.Equal(5, order.StatusHistory.Count);

        // Estado final impede cancelamento ou nova transição
        var cancelCompleted = order.Cancel(actor, "Tarde demais", utc0.AddMinutes(15));
        Assert.True(cancelCompleted.IsFailure);
        Assert.Equal("Order.CannotCancelCompleted", cancelCompleted.Error.Code);
    }

    [Fact]
    public void Order_Cancel_ShouldRequireReasonAndBlockFurtherTransitions()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero);
        var cart = ShoppingCart.Create(tenantId, "sess-2", utc0.AddDays(1)).Value;
        cart.AddItem(CartItemKind.CatalogProduct, Guid.NewGuid(), null, null, null, null, "X-Burger", "Padrão", "{}", Money.Create(30m).Value, 1, false, null, utc0);

        var order = Order.CreateFromCart(
            cart, "#0002", OrderChannel.OwnDeliveryMenu, FulfillmentMode.Delivery,
            "João", null, "Rua A, 10", Money.Create(5m).Value, Money.Zero(), Money.Zero(), null, null, null, null, utc0).Value;

        var emptyReason = order.Cancel(Guid.NewGuid(), " ", utc0.AddMinutes(1));
        Assert.True(emptyReason.IsFailure);
        Assert.Equal("Order.CancellationReasonRequired", emptyReason.Error.Code);

        Assert.True(order.Cancel(Guid.NewGuid(), "Cliente cancelou", utc0.AddMinutes(2)).IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);

        var advanceAfterCancel = order.TransitionTo(OrderStatus.Accepted, Guid.NewGuid(), "Erro", utc0.AddMinutes(3));
        Assert.True(advanceAfterCancel.IsFailure);
        Assert.Equal("Order.TerminalStatus", advanceAfterCancel.Error.Code);
    }
}
