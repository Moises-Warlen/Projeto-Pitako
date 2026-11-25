using Pitako.Modules.Printing.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class PrintingDomainTests
{
    [Fact]
    public void PrintableStoreIdentity_ShouldRequireLogoAndCnpj_AndForbidPitakoBranding()
    {
        var tenantId = Guid.NewGuid();
        const string validCnpj = "11222333000181";

        // Sem logo falha
        var noLogo = PrintableStoreIdentity.Create(tenantId, "Açaí do Sul", "Açaí do Sul LTDA", validCnpj, null);
        Assert.True(noLogo.IsFailure);
        Assert.Equal("Print.StoreLogoRequired", noLogo.Error.Code);

        // CNPJ inválido falha
        var badCnpj = PrintableStoreIdentity.Create(tenantId, "Açaí do Sul", "Açaí do Sul LTDA", "12345678000100", "https://cdn.loja/logo.png");
        Assert.True(badCnpj.IsFailure);

        // Marca PITAKO proibida em impressões operacionais da loja
        var pitakoBrand = PrintableStoreIdentity.Create(tenantId, "PITAKO", "PITAKO SaaS", validCnpj, "https://cdn.loja/pitako-logo.png");
        Assert.True(pitakoBrand.IsFailure);
        Assert.Equal("Print.PitakoBrandingForbidden", pitakoBrand.Error.Code);

        var ok = PrintableStoreIdentity.Create(
            tenantId,
            "Açaí do Sul",
            "Açaí do Sul Comercio LTDA",
            validCnpj,
            "https://cdn.acaisul.com/logo.png",
            "Rua das Flores, 100",
            "(11) 99999-0000");

        Assert.True(ok.IsSuccess);
        Assert.Equal("11.222.333/0001-81", ok.Value.Cnpj.Formatted);
    }

    [Fact]
    public void ThermalTicketRenderer_ShouldIncludeStoreLogoAndCnpj_HighlightNotes_AndEmitValidEscPos()
    {
        var tenantId = Guid.NewGuid();
        var identity = PrintableStoreIdentity.Create(
            tenantId,
            "Sabor & Açaí",
            "Sabor Comercio de Alimentos LTDA",
            "11222333000181",
            "https://cdn.sabor.com/logo.png").Value;

        var payload = new ThermalTicketPayloadInput(
            ProductionSectorType.Production,
            PrintDocumentKind.ProductionKitchenTicket,
            "#0088",
            "QR Code Mesa",
            "Mariana",
            "Mesa M04",
            null,
            new DateTimeOffset(2026, 11, 25, 21, 15, 0, TimeSpan.Zero),
            [
                new TicketLineInput("Açaí 500ml", 1, "Base: Tradicional | Grátis: Morango | Adicionais: Leite Ninho", "Sem granola"),
                new TicketLineInput("Suco Laranja 400ml", 2, "Sem açúcar")
            ]);

        var (text, escPosB64) = ThermalTicketRenderer.Render(identity, payload, columnWidth: 48);

        Assert.Contains("SABOR & AÇAÍ", text);
        Assert.Contains("CNPJ: 11.222.333/0001-81", text);
        Assert.Contains("[LOGO LOJA:", text);
        Assert.Contains("NAO SUBSTITUI DOCUMENTO FISCAL", text);
        Assert.Contains("!! OBS: SEM GRANOLA !!", text);
        Assert.DoesNotContain("PITAKO", text, StringComparison.OrdinalIgnoreCase);

        var packet = Convert.FromBase64String(escPosB64);
        Assert.Equal(0x1B, packet[0]);
        Assert.Equal(0x40, packet[1]);
        Assert.Equal(0x1D, packet[^3]);
        Assert.Equal(0x56, packet[^2]);
        Assert.Equal(0x01, packet[^1]);
    }
}
