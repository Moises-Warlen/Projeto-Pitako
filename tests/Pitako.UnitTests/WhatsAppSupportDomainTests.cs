using Pitako.Modules.WhatsAppSupport.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class WhatsAppSupportDomainTests
{
    [Fact]
    public void TraditionalMenuBotRule_ShouldMatchExactAndContainsModes()
    {
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var exactRule = TraditionalMenuBotRule.Create(tenantId, channelId, "Opção 1", "1", BotRuleMatchMode.Exact, "Cardápio", includeMenuLink: true).Value;
        var containsRule = TraditionalMenuBotRule.Create(tenantId, channelId, "Falar com Humano", "atendente", BotRuleMatchMode.Contains, "Transferindo...", escalateToHuman: true).Value;

        Assert.True(exactRule.Matches(" 1 "));
        Assert.False(exactRule.Matches("quero 1 açaí"));
        Assert.True(containsRule.Matches("Por favor chamar um ATENDENTE agora"));
    }

    [Fact]
    public void AiGroundingValidator_ShouldAllowVerifiedFacts_AndBlockHallucinatedPricesOrOrders()
    {
        var snapshot = new GroundedTenantKnowledgeSnapshot(
            Guid.NewGuid(),
            "Açaí do Sul",
            "10:00 às 22:00",
            "https://acaisul.pitako.app",
            [new GroundedMenuItem("Açaí 500ml", 24.90m, true, "Açaís")],
            ["BEMVINDO10"],
            new GroundedOrderFact("#0042", "InPreparation", 34.90m));

        var valid = AiGroundingValidator.ValidateResponse(
            snapshot,
            "Olá! O Açaí 500ml custa R$ 24,90 e seu pedido #0042 de R$ 34,90 está em preparo.");
        Assert.True(valid.IsSuccess);

        var fabricatedPrice = AiGroundingValidator.ValidateResponse(
            snapshot,
            "Temos promoção relâmpago hoje por R$ 99,99!");
        Assert.True(fabricatedPrice.IsFailure);
        Assert.Equal("AiGrounding.UnverifiedPriceClaim", fabricatedPrice.Error.Code);

        var fabricatedOrder = AiGroundingValidator.ValidateResponse(
            snapshot,
            "Seu pedido #9999 já saiu para entrega.");
        Assert.True(fabricatedOrder.IsFailure);
        Assert.Equal("AiGrounding.UnverifiedOrderReference", fabricatedOrder.Error.Code);
    }
}
