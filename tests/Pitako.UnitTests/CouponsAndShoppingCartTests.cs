using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.Orders.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class CouponsAndShoppingCartTests
{
    [Fact]
    public void Coupon_ShouldValidateWindowMinimumLimitsEligibilityPromotionsAndReversals()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var eligibleProdId = Guid.NewGuid();
        var otherProdId = Guid.NewGuid();

        var coupon = Coupon.Create(
            tenantId,
            " acai 10 ",
            CouponDiscountType.Percentage,
            15m,
            utc0.AddDays(-1),
            utc0.AddDays(5),
            minimumOrderAmount: Money.Create(40.00m).Value,
            maxDiscountAmount: Money.Create(12.00m).Value,
            maxTotalUses: 2,
            maxUsesPerCustomer: 1,
            canCombineWithPromotions: false).Value;

        coupon.AddEligibilityRule(CouponTargetType.Product, eligibleProdId);
        Assert.Equal("ACAI10", coupon.Code);

        // Produto não elegível -> rejeitado
        var notEligible = coupon.Evaluate(
            utc0,
            "cliente@teste.com",
            [new CouponBasketLine(otherProdId, null, Money.Create(100m).Value)],
            Money.Create(8m).Value);
        Assert.True(notEligible.IsFailure);
        Assert.Equal("Coupon.NoEligibleItems", notEligible.Error.Code);

        // Item elegível mas com promoção não combinável -> rejeitado
        var promoConflict = coupon.Evaluate(
            utc0,
            "cliente@teste.com",
            [new CouponBasketLine(eligibleProdId, null, Money.Create(60m).Value, HasNonCombinablePromotion: true)],
            Money.Create(8m).Value);
        Assert.True(promoConflict.IsFailure);
        Assert.Equal("Coupon.NoEligibleItems", promoConflict.Error.Code);

        // Abaixo do mínimo -> rejeitado
        var underMin = coupon.Evaluate(
            utc0,
            "cliente@teste.com",
            [new CouponBasketLine(eligibleProdId, null, Money.Create(35m).Value)],
            Money.Create(8m).Value);
        Assert.True(underMin.IsFailure);
        Assert.Equal("Coupon.MinimumOrderNotReached", underMin.Error.Code);

        // Elegível com teto de desconto R$12,00 (15% de 100 = 15 -> limitado a 12)
        var valid = coupon.Evaluate(
            utc0,
            "cliente@teste.com",
            [new CouponBasketLine(eligibleProdId, null, Money.Create(100m).Value)],
            Money.Create(8m).Value);
        Assert.True(valid.IsSuccess);
        Assert.Equal(12.00m, valid.Value.ItemsDiscount.Amount);
        Assert.Equal(0m, valid.Value.DeliveryDiscount.Amount);

        // Resgate por cliente e bloqueio na segunda tentativa
        var orderId = Guid.NewGuid();
        coupon.RecordRedemption(orderId, "cliente@teste.com", valid.Value.TotalDiscount, utc0);
        var secondUseSameCustomer = coupon.Evaluate(
            utc0,
            "CLIENTE@TESTE.COM",
            [new CouponBasketLine(eligibleProdId, null, Money.Create(100m).Value)],
            Money.Zero());
        Assert.True(secondUseSameCustomer.IsFailure);
        Assert.Equal("Coupon.CustomerLimitReached", secondUseSameCustomer.Error.Code);

        // Estorno do pedido libera o limite do cliente novamente
        Assert.True(coupon.ReverseRedemptionForOrder(orderId, utc0.AddMinutes(10)));
        var afterReversal = coupon.Evaluate(
            utc0.AddMinutes(11),
            "cliente@teste.com",
            [new CouponBasketLine(eligibleProdId, null, Money.Create(50m).Value)],
            Money.Zero());
        Assert.True(afterReversal.IsSuccess);
        Assert.Equal(7.50m, afterReversal.Value.ItemsDiscount.Amount);
    }

    [Fact]
    public void Coupon_FreeShipping_ShouldDiscountOnlyDeliveryFeeUpToCap()
    {
        var utc0 = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var shipCoupon = Coupon.Create(
            Guid.NewGuid(),
            "FRETEGRATIS",
            CouponDiscountType.FreeShipping,
            0m,
            utc0.AddHours(-1),
            utc0.AddHours(10),
            Money.Create(30m).Value,
            maxDiscountAmount: Money.Create(10m).Value).Value;

        var eval = shipCoupon.Evaluate(
            utc0,
            "cli1",
            [new CouponBasketLine(Guid.NewGuid(), null, Money.Create(50m).Value)],
            Money.Create(14.50m).Value);

        Assert.True(eval.IsSuccess);
        Assert.Equal(0m, eval.Value.ItemsDiscount.Amount);
        Assert.Equal(10.00m, eval.Value.DeliveryDiscount.Amount);
    }

    [Fact]
    public void ShoppingCart_ShouldKeepDistinctCustomizedItemsSeparate_AndOnlyMergeIdenticalNonWeightedItems()
    {
        var tenantId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var cart = ShoppingCart.Create(tenantId, "sess-abc", utcNow.AddDays(1)).Value;
        var cup500Id = Guid.NewGuid();

        // Dois copos de açaí 500 ml com personalizações diferentes DEVEM permanecer como 2 linhas separadas
        var cup1 = cart.AddItem(
            CartItemKind.CustomAcaiCup, null, null, null, cup500Id, null,
            "Açaí 500 ml", "Grátis: Leite em pó, Granola | Adicionais: Nutella", "{\"free\":[\"leite\",\"granola\"]}",
            Money.Create(28.00m).Value, 1, false, "Copo da Maria", utcNow);

        var cup2 = cart.AddItem(
            CartItemKind.CustomAcaiCup, null, null, null, cup500Id, null,
            "Açaí 500 ml", "Grátis: Paçoca, Morango", "{\"free\":[\"pacoca\",\"morango\"]}",
            Money.Create(22.00m).Value, 1, false, "Copo do João", utcNow);

        Assert.True(cup1.IsSuccess);
        Assert.True(cup2.IsSuccess);
        Assert.Equal(2, cart.Items.Count);
        Assert.NotEqual(cup1.Value.Id, cup2.Value.Id);
        Assert.Equal(50.00m, cart.ItemsSubtotal.Amount);

        // Adicionar exatamente o mesmo copo 2 novamente soma a quantidade mantendo 2 itens distintos no carrinho
        var cup2Repeat = cart.AddItem(
            CartItemKind.CustomAcaiCup, null, null, null, cup500Id, null,
            "Açaí 500 ml", "Grátis: Paçoca, Morango", "{\"free\":[\"pacoca\",\"morango\"]}",
            Money.Create(22.00m).Value, 1, false, "Copo do João", utcNow);

        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(2, cup2Repeat.Value.Quantity);
        Assert.Equal(72.00m, cart.ItemsSubtotal.Amount);
    }
}
