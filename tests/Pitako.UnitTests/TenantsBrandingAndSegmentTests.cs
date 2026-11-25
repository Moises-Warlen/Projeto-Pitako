using Pitako.Modules.Tenants.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class TenantsBrandingAndSegmentTests
{
    [Theory]
    [InlineData("pizzaria", false, false, true)]
    [InlineData("acaiteria", true, true, false)]
    [InlineData("hamburgueria", false, false, false)]
    [InlineData("padaria", false, true, false)]
    [InlineData("sorveteria", true, true, false)]
    [InlineData("restaurante", false, true, false)]
    [InlineData("adega", false, false, false)]
    [InlineData("lanchonete", false, false, false)]
    [InlineData("pastelaria", false, false, false)]
    [InlineData("doceria", false, true, false)]
    [InlineData("cafeteria-especial", false, false, false)]
    public void BusinessSegmentCatalog_ShouldSupportAllCoreAndFutureSegments(
        string segmentCode,
        bool expectedAcai,
        bool expectedScale,
        bool expectedFractional)
    {
        var result = BusinessSegmentCatalog.Resolve(segmentCode);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAcai, result.Value.SupportsAcaiBuilder);
        Assert.Equal(expectedScale, result.Value.SupportsWeightScale);
        Assert.Equal(expectedFractional, result.Value.SupportsFractionalItems);
    }

    [Fact]
    public void StoreVisualIdentity_ShouldNotForcePitakoColors_AndSupportDraftPreviewBeforePublish()
    {
        var tenantId = Guid.NewGuid();
        var identity = StoreVisualIdentity.CreateDefault(tenantId);

        // Identidade neutra inicial não impõe vermelho nem logo PITAKO
        Assert.Null(identity.PublishedLogoUrl);
        Assert.NotEqual("#FF0000", identity.PublishedPrimaryColorHex);

        var draftUpdate = identity.UpdateDraftBranding(
            "https://cdn.loja.com/logo-roxa.png",
            "https://cdn.loja.com/capa.jpg",
            "O melhor açaí do bairro",
            "#5B21B6",
            "#FAF5FF",
            "#F59E0B",
            "showcase",
            [new StoreBannerSnapshot("Promo Verão", "https://cdn.loja.com/banner1.jpg", "/acai", 1)]);

        Assert.True(draftUpdate.IsSuccess);
        Assert.True(identity.HasUnpublishedChanges);

        // Pré-visualização reflete rascunho; visão publicada permanece intacta até publicar
        var preview = identity.GetDraftPreview();
        var publishedBefore = identity.GetPublishedView();
        Assert.Equal("#5B21B6", preview.PrimaryColorHex);
        Assert.Single(preview.Banners);
        Assert.Null(publishedBefore.LogoUrl);
        Assert.Empty(publishedBefore.Banners);

        identity.Publish(DateTimeOffset.UtcNow);
        var publishedAfter = identity.GetPublishedView();
        Assert.Equal(1, identity.PublishedVersion);
        Assert.False(identity.HasUnpublishedChanges);
        Assert.Equal("https://cdn.loja.com/logo-roxa.png", publishedAfter.LogoUrl);
        Assert.Equal("#5B21B6", publishedAfter.PrimaryColorHex);
        Assert.Single(publishedAfter.Banners);
    }

    [Fact]
    public void StoreVisualIdentity_ShouldRejectInvalidHexColor()
    {
        var identity = StoreVisualIdentity.CreateDefault(Guid.NewGuid());
        var res = identity.UpdateDraftBranding(null, null, null, "vermelho", "#FFFFFF", "#000000", "grid");

        Assert.True(res.IsFailure);
        Assert.Equal("Branding.InvalidColor", res.Error.Code);
    }
}
