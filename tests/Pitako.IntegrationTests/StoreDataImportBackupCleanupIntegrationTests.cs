using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Pitako.Api;
using Pitako.Api.Bootstrap;
using Pitako.Api.Composition;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class StoreDataImportBackupCleanupIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _app = PitakoApiHostBuilder.BuildWebApplication(
            ["--environment", "Development"],
            sharedSqliteConnection: _connection,
            listenUrl: "http://127.0.0.1:0");

        await _app.Services.EnsureDatabaseInitializedAsync(useSqlite: true);
        await PitakoDevDemoSeeder.SeedIfNeededAsync(_app.Services);
        await _app.StartAsync();

        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SpreadsheetImport_BackupImport_StarterCleanup_MultiTenantAndRbac_ShouldWorkAcrossStores()
    {
        // 1. Login seeded store (Açaí Tropical Express) & create a SECOND brand-new store (Pizzaria Bella Italia)
        var acaiAdminToken = await LoginEstablishmentAsync("acai-demo", "admin@tropicalexpress.com.br", "Store@12345");
        var acaiCashierToken = await LoginEstablishmentAsync("acai-demo", "caixa@tropicalexpress.com.br", "Caixa@12345");
        var masterToken = await LoginMasterAsync();

        // Provisionar nova loja multi-segmento via Master
        var newSubRes = await PostJsonAuthAsync(masterToken, "/api/v1/master/customers-subscriptions", new
        {
            customerName = "Bella Italia Massas Ltda",
            documentNumber = "11444777000161",
            billingEmail = "bella@italia.com.br",
            phone = "16998887766",
            planId = await GetFirstPlanIdAsync(masterToken),
            startWithTrial = false
        });
        Assert.Equal(HttpStatusCode.Created, newSubRes.StatusCode);
        var custId = (await newSubRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();

        var newTenantRes = await PostJsonAuthAsync(masterToken, "/api/v1/store/establishments", new
        {
            customerAccountId = custId,
            legalName = "Bella Italia Massas Ltda",
            tradeName = "Pizzaria Bella Italia",
            slug = "bella-pizza",
            cnpj = "22.333.444/0001-81",
            segmentCode = "pizzaria"
        });
        Assert.Equal(HttpStatusCode.Created, newTenantRes.StatusCode);
        var pizzaTenantId = (await newTenantRes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("tenant").GetProperty("id").GetGuid();

        var createPizzaAdminRes = await PostJsonAuthAsync(masterToken, $"/api/v1/master/establishments/{pizzaTenantId}/admin-user", new
        {
            fullName = "Chef Mario",
            email = "mario@bellapizza.com.br",
            password = "Pizza@12345",
            role = 1
        });
        Assert.Equal(HttpStatusCode.Created, createPizzaAdminRes.StatusCode);

        var pizzaAdminToken = await LoginEstablishmentAsync("bella-pizza", "mario@bellapizza.com.br", "Pizza@12345");

        // 2. Cashier cannot run starter cleanup or import (RBAC)
        var cashierCleanupAttempt = await PostJsonAuthAsync(acaiCashierToken, "/api/v1/store-data/starter-cleanup/execute", new
        {
            confirmPhrase = "LIMPAR",
            createSafetyBackup = true,
            removeProducts = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, cashierCleanupAttempt.StatusCode);

        // 3. Create real order in acai-demo before cleanup so history preservation is proved
        var cartRes = await _client!.PostAsJsonAsync("/api/v1/public/store/acai-demo/cart/sess-hist-test/items", new
        {
            kind = "acai",
            acaiCup = new
            {
                cupSizeId = await GetFirstAcaiCupIdAsync("acai-demo"),
                baseFlavorIds = new[] { await GetFirstFlavorIdAsync("acai-demo") },
                freeToppings = Array.Empty<object>(),
                paidAddons = Array.Empty<object>()
            },
            quantity = 1
        });
        Assert.Equal(HttpStatusCode.OK, cartRes.StatusCode);
        var checkoutRes = await _client.PostAsJsonAsync("/api/v1/public/store/acai-demo/checkout", new
        {
            cartSessionKey = "sess-hist-test",
            channel = 1,
            fulfillmentMode = 2,
            customerName = "Cliente Histórico",
            customerPhone = "16999990000",
            paymentMethod = "PIX",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            lgpdConsentAccepted = true
        });
        Assert.Equal(HttpStatusCode.Created, checkoutRes.StatusCode);

        // 4. Download CSV template & batch upload PNG photo on new store (Pizzaria Bella Italia)
        using var tplReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/store-data/import/template?format=csv");
        tplReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pizzaAdminToken);
        var tplResp = await _client.SendAsync(tplReq);
        Assert.Equal(HttpStatusCode.OK, tplResp.StatusCode);

        var fakePngBase64 = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]);
        var imgBatchRes = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/import/images-batch", new
        {
            files = new[] { new { fileName = "pizza-quatro-queijos.png", base64DataUrl = fakePngBase64 } }
        });
        Assert.Equal(HttpStatusCode.OK, imgBatchRes.StatusCode);
        var imgBatchJson = await imgBatchRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, imgBatchJson.GetProperty("uploadedCount").GetInt32());
        var uploadedImgArr = imgBatchJson.GetProperty("images");

        // 5. Spreadsheet preview & confirm creating product + technical sheet + variations + modifiers + photo
        var customCsv = """
            SKU;Categoria;Produto;Descricao;Preco;PrecoPromocional;Variacoes;Ingredientes;CustosIngredientes;Unidades;Adicionais;PesoPorcao;Tags;CodigoFoto
            QQ-900;Pizzas Especiais;Pizza Quatro Queijos Artesanal;Mussarela, provolone, gorgonzola e parmesão;58.90;49.90;Brotinho:34.90|Grande:58.90;Massa Italiana:300:0.015|Mix Quatro Queijos:220:0.045;0.015|0.045;g|g;Bordas:Borda Catupiry,Borda Cheddar;650 g;artesanal,destaque;pizza-quatro-queijos.png
            """;
        var prevRes = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/import/spreadsheet/preview", new
        {
            fileName = "cardapio-pizzaria.csv",
            fileBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(customCsv))
        });
        Assert.Equal(HttpStatusCode.OK, prevRes.StatusCode);
        var prevJson = await prevRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, prevJson.GetProperty("newCount").GetInt32());

        var confirmRes = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/import/spreadsheet/confirm", new
        {
            sourceLabel = "Planilha Pizza Teste",
            duplicateMode = "skip",
            allowPriceAndSheetOverwrite = false,
            rows = prevJson.GetProperty("rows"),
            imageMappings = uploadedImgArr
        });
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var confirmJson = await confirmRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, confirmJson.GetProperty("createdCount").GetInt32());
        Assert.Equal(1, confirmJson.GetProperty("sheetsCreatedOrUpdated").GetInt32());
        Assert.Equal(1, confirmJson.GetProperty("imagesAttached").GetInt32());

        // 6. Verify public menu of Pizzaria exposes imported ingredients & photo & never leaks into acai-demo
        var pizzaMenuRes = await _client.GetAsync("/api/v1/public/store/bella-pizza/menu");
        Assert.Equal(HttpStatusCode.OK, pizzaMenuRes.StatusCode);
        var pizzaMenuJson = await pizzaMenuRes.Content.ReadFromJsonAsync<JsonElement>();
        var importedProd = pizzaMenuJson.GetProperty("products").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("name").GetString() == "Pizza Quatro Queijos Artesanal");
        Assert.NotEqual(JsonValueKind.Undefined, importedProd.ValueKind);
        Assert.True(importedProd.GetProperty("ingredients").GetArrayLength() >= 2);
        Assert.Contains("/api/v1/public/media/", importedProd.GetProperty("primaryImageUrl").GetString());

        var acaiMenuAfter = await (await _client.GetAsync("/api/v1/public/store/acai-demo/menu")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(acaiMenuAfter.GetProperty("products").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "Pizza Quatro Queijos Artesanal");

        // 7. Re-importing duplicate with skip=true must skip (not duplicate)
        var duplicateRes = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/import/spreadsheet/confirm", new
        {
            sourceLabel = "Planilha Duplicada",
            duplicateMode = "skip",
            allowPriceAndSheetOverwrite = false,
            rows = prevJson.GetProperty("rows")
        });
        var dupJson = await duplicateRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, dupJson.GetProperty("createdCount").GetInt32());
        Assert.Equal(1, dupJson.GetProperty("skippedCount").GetInt32());

        // 8. Backup Import (Adapter JSON)
        var extBackup = """
            {
              "groups": [
                { "name": "Bebidas Italianas", "items": [{ "sku": "SODA-IT", "name": "Soda Italiana Maçã Verde", "price": 14.50 }] }
              ]
            }
            """;
        var bkpAnalyze = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/backup-import/analyze", new
        {
            fileName = "backup-terceiro.json",
            fileBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(extBackup))
        });
        Assert.Equal(HttpStatusCode.OK, bkpAnalyze.StatusCode);
        var bkpJson = await bkpAnalyze.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("menu-json-generic", bkpJson.GetProperty("adapterId").GetString());

        var bkpConfirm = await PostJsonAuthAsync(pizzaAdminToken, "/api/v1/store-data/backup-import/confirm", new
        {
            adapterId = "menu-json-generic",
            duplicateMode = "skip",
            allowPriceAndSheetOverwrite = false,
            rows = bkpJson.GetProperty("previewRows")
        });
        Assert.Equal(HttpStatusCode.OK, bkpConfirm.StatusCode);

        // 9. Starter Cleanup preserves real Orders on acai-demo while generating safety backup and clearing starter catalog
        var cleanupPrevHttp = await GetAuthAsync(acaiAdminToken, "/api/v1/store-data/starter-cleanup/preview");
        var cleanupPrevRaw = await cleanupPrevHttp.Content.ReadAsStringAsync();
        Assert.True(cleanupPrevHttp.IsSuccessStatusCode, $"starter-cleanup/preview failed ({cleanupPrevHttp.StatusCode}): {cleanupPrevRaw}");
        var cleanupPrev = JsonDocument.Parse(cleanupPrevRaw).RootElement;
        var protectedOrdersBefore = cleanupPrev.GetProperty("protectedOrdersCount").GetInt32();
        Assert.True(protectedOrdersBefore >= 1);

        var cleanupExec = await PostJsonAuthAsync(acaiAdminToken, "/api/v1/store-data/starter-cleanup/execute", new
        {
            confirmPhrase = "LIMPAR",
            createSafetyBackup = true,
            removeCombos = true,
            removePromotions = true,
            removeTechnicalSheets = true,
            removeProducts = true,
            removeModifiers = true,
            removeCategories = true,
            removeSupplies = true
        });
        Assert.Equal(HttpStatusCode.OK, cleanupExec.StatusCode);
        var clnExecJson = await cleanupExec.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(clnExecJson.GetProperty("safetyBackupCode").GetString()));
        Assert.True(clnExecJson.GetProperty("ordersPreserved").GetBoolean());

        // Real orders count remains intact AND Pizzaria Bella Italia catalog was completely untouched!
        var cleanupAfter = await (await GetAuthAsync(acaiAdminToken, "/api/v1/store-data/starter-cleanup/preview")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(protectedOrdersBefore, cleanupAfter.GetProperty("protectedOrdersCount").GetInt32());

        var pizzaMenuFinal = await (await _client.GetAsync("/api/v1/public/store/bella-pizza/menu")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(pizzaMenuFinal.GetProperty("products").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "Pizza Quatro Queijos Artesanal");
    }

    private async Task<string> LoginEstablishmentAsync(string slug, string email, string password)
    {
        var r = await _client!.PostAsJsonAsync("/api/v1/auth/login-store", new { storeSlug = slug, email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<string> LoginMasterAsync()
    {
        var r = await _client!.PostAsJsonAsync("/api/v1/auth/login-saas", new { email = "master@pitako.com.br", password = "Master@12345" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> GetFirstPlanIdAsync(string masterToken)
    {
        var res = await GetAuthAsync(masterToken, "/api/v1/master/plans");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetFirstAcaiCupIdAsync(string slug)
    {
        var json = await (await _client!.GetAsync($"/api/v1/public/store/{slug}/menu")).Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("acai").GetProperty("cupSizes").EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetFirstFlavorIdAsync(string slug)
    {
        var json = await (await _client!.GetAsync($"/api/v1/public/store/{slug}/menu")).Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("acai").GetProperty("baseFlavors").EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> GetAuthAsync(string token, string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client!.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostJsonAuthAsync(string token, string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client!.SendAsync(req);
    }
}
