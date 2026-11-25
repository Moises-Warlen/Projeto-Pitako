using Pitako.Modules.Catalog.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class CatalogDomainTests
{
    [Fact]
    public void Product_Paused_ShouldNotAcceptOrders_And_HistoryShouldPreventPhysicalDeletion()
    {
        var tenantId = Guid.NewGuid();
        var category = Category.Create(tenantId, "Burgers").Value;
        var product = Product.Create(tenantId, category.Id, "X-Pitako", Money.Create(28.00m).Value).Value;

        Assert.True(product.CanReceiveOrders());

        product.Pause();
        Assert.False(product.CanReceiveOrders());

        Assert.True(product.Reactivate().IsSuccess);
        Assert.True(product.CanReceiveOrders());

        // Produto com histórico é arquivado preservando dados históricos
        var outcomeWithHistory = product.PrepareDeletion(hasHistoricalReferences: true);
        Assert.Equal(DeletionOutcome.ArchivedPreservingHistory, outcomeWithHistory);
        Assert.Equal(CatalogItemStatus.Archived, product.Status);
        Assert.False(product.CanReceiveOrders());
        Assert.True(product.Reactivate().IsFailure);

        // Produto sem histórico pode ser excluído fisicamente
        var freshProduct = Product.Create(tenantId, category.Id, "Novo Item", Money.Create(10m).Value).Value;
        Assert.Equal(DeletionOutcome.PhysicallyDeleted, freshProduct.PrepareDeletion(hasHistoricalReferences: false));
    }

    [Fact]
    public void Product_ShouldMaintainSinglePrimaryImage_AndComboShouldRequireActiveProducts()
    {
        var tenantId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var burger = Product.Create(tenantId, catId, "Burger", Money.Create(25m).Value).Value;
        var soda = Product.Create(tenantId, catId, "Refrigerante", Money.Create(8m).Value).Value;

        var img1 = burger.AddImage("https://img.com/1.jpg", 1, isPrimary: true).Value;
        var img2 = burger.AddImage("https://img.com/2.jpg", 2, isPrimary: true).Value;
        Assert.False(img1.IsPrimary);
        Assert.True(img2.IsPrimary);

        burger.SetPrimaryImage(img1.Id);
        Assert.True(img1.IsPrimary);
        Assert.False(img2.IsPrimary);

        var combo = Combo.Create(tenantId, "Combo Duplo", Money.Create(30m).Value).Value;
        combo.AddItem(burger.Id, 1);
        combo.AddItem(soda.Id, 1);

        var productsMap = new Dictionary<Guid, Product> { [burger.Id] = burger, [soda.Id] = soda };
        Assert.True(combo.CanReceiveOrders(productsMap));

        // Pausar um item do combo impede pedidos do combo
        soda.Pause();
        Assert.False(combo.CanReceiveOrders(productsMap));
    }

    [Fact]
    public void Promotion_ShouldRespectTimeWindow_AndNeverProduceNegativePrice()
    {
        var tenantId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var prod = Product.Create(tenantId, catId, "Açaí 300ml", Money.Create(15m).Value).Value;
        var start = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(6);

        var promo = Promotion.Create(tenantId, "Happy Hour", PromotionDiscountType.FixedAmount, 20m, start, end).Value;
        promo.AddTarget(PromotionTargetType.Category, catId);

        Assert.False(promo.IsActiveAt(start.AddMinutes(-1)));
        Assert.True(promo.IsActiveAt(start.AddHours(1)));
        Assert.False(promo.IsActiveAt(end.AddMinutes(1)));
        Assert.True(promo.AppliesTo(prod));
        Assert.Equal(0m, promo.CalculatePromotionalPrice(prod.BasePrice).Amount);
    }
}
