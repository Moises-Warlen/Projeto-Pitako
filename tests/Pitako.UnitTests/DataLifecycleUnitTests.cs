using Pitako.Modules.DataLifecycle.Application.Parsers;
using Pitako.Modules.DataLifecycle.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class DataLifecycleUnitTests
{
    [Fact]
    public void LegacyPosMigrationParser_ShouldSupportConsumerGoomerAndSaipos_WithDedupAndRowErrors()
    {
        // 1. Consumer (; + pt-BR preço + SKU duplicado + linha preço inválido)
        const string consumerCsv = """
            codigo_pdv;categoria;nome_produto;preco;descricao
            C-101;Açaís;Açaí 500ml;24,90;Tradicional
            C-101;Açaís;Açaí 500ml Duplicado;24,90;Repetido
            C-102;Bebidas;Suco Natural;abc;Inválido
            """;

        var cRes = LegacyPosMigrationParser.ParseCatalogCsv(DataMigrationSourceSystem.Consumer, consumerCsv);
        Assert.True(cRes.IsSuccess);
        Assert.Equal(3, cRes.Value.TotalRows);
        Assert.Equal(1, cRes.Value.ValidCount);
        Assert.Equal(1, cRes.Value.SkippedDuplicateCount);
        Assert.Equal(1, cRes.Value.FailedCount);
        Assert.Equal(24.90m, cRes.Value.ValidItems[0].UnitPriceAmount);

        // 2. Goomer (,)
        const string goomerCsv = """
            sku,category,title,price
            G-01,Lanches,Burger Artesanal,32.50
            G-02,Sobremesas,Brownie,14.00
            """;
        var gRes = LegacyPosMigrationParser.ParseCatalogCsv(DataMigrationSourceSystem.Goomer, goomerCsv);
        Assert.True(gRes.IsSuccess);
        Assert.Equal(2, gRes.Value.ValidCount);

        // 3. Saipos (;)
        const string saiposCsv = """
            codigo;grupo;descricao;valor_venda
            S-90;Salgados;Coxinha Frango;R$ 9,50
            """;
        var sRes = LegacyPosMigrationParser.ParseCatalogCsv(DataMigrationSourceSystem.Saipos, saiposCsv);
        Assert.True(sRes.IsSuccess);
        Assert.Equal(9.50m, sRes.Value.ValidItems[0].UnitPriceAmount);
    }

    [Fact]
    public void TenantBackupSnapshot_ShouldDetectTamperedSha256_AndLgpdAnonymizerShouldBeDeterministic()
    {
        var tenantId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 22, 0, 0, TimeSpan.Zero);

        var snap = TenantBackupSnapshot.Create(tenantId, "BKP-0001", "v1:encrypted", "expectedhash", 512, true, true, true, Guid.NewGuid(), utc0).Value;
        var bad = snap.VerifyAndMarkRestored("tamperedhash", Guid.NewGuid(), utc0.AddMinutes(5));
        Assert.True(bad.IsFailure);
        Assert.Equal("Backup.IntegrityCheckFailed", bad.Error.Code);
        Assert.Equal(TenantBackupStatus.IntegrityFailed, snap.Status);

        var token1 = LgpdAnonymizer.GenerateAnonymousToken(tenantId, "cliente@email.com");
        var token2 = LgpdAnonymizer.GenerateAnonymousToken(tenantId, "CLIENTE@email.com");
        Assert.StartsWith("ANON-", token1);
        Assert.Equal(token1, token2);
        Assert.Equal("cl***om", LgpdAnonymizer.MaskIdentifier("cliente@email.com"));
    }
}
