using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Pitako.Api;
using Pitako.Api.Bootstrap;
using Pitako.Api.Composition;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class UniversalMenuEndToEndIntegrationTests : IAsyncLifetime
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

    private static HttpRequestMessage AuthReq(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task UniversalMenu_FullFlow_MultiTenantIsolation_InPlaceCartEdit_IdempotentStockWithLoss_AndCancellationReversal()
    {
        // 1. Login Master + criar Cliente + 2 Lojas isoladas (Pizzaria + Hamburgueria)
        var masterLogin = await _client!.PostAsJsonAsync("/api/v1/auth/login-saas", new
        {
            email = "master@pitako.com.br",
            password = "Master@12345"
        });
        Assert.Equal(HttpStatusCode.OK, masterLogin.StatusCode);
        var masterToken = (await masterLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var plansRes = await _client!.SendAsync(AuthReq(HttpMethod.Get, "/api/v1/master/plans", masterToken));
        Assert.Equal(HttpStatusCode.OK, plansRes.StatusCode);
        var plansJson = await plansRes.Content.ReadFromJsonAsync<JsonElement>();
        var firstPlanId = plansJson.EnumerateArray().First(p => p.GetProperty("unlimitedEstablishments").GetBoolean()).GetProperty("id").GetGuid();

        var custRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/master/customers-subscriptions", masterToken, new
        {
            customerName = "Rede Multi Segmentos",
            documentNumber = "44555666000181",
            billingEmail = "rede@multisegmentos.com.br",
            phone = "16999990001",
            planId = firstPlanId,
            startWithTrial = false
        }));
        Assert.Equal(HttpStatusCode.Created, custRes.StatusCode);
        var customerId = (await custRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();

        var storePizzariaRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/store/establishments", masterToken, new
        {
            customerAccountId = customerId,
            legalName = "Pizzaria Forno Nobre LTDA",
            tradeName = "Forno Nobre Pizzaria",
            slug = "forno-nobre-e2e",
            cnpj = "55.666.777/0001-81",
            segmentCode = "pizzaria"
        }));
        Assert.Equal(HttpStatusCode.Created, storePizzariaRes.StatusCode);
        var pizzariaId = (await storePizzariaRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();

        var storeBurgerRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/store/establishments", masterToken, new
        {
            customerAccountId = customerId,
            legalName = "Burger Brass LTDA",
            tradeName = "Burger Brass",
            slug = "burger-brass-e2e",
            cnpj = "66.777.888/0001-81",
            segmentCode = "hamburgueria"
        }));
        Assert.Equal(HttpStatusCode.Created, storeBurgerRes.StatusCode);
        var burgerId = (await storeBurgerRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();

        await _client.SendAsync(AuthReq(HttpMethod.Post, $"/api/v1/master/establishments/{pizzariaId}/admin-user", masterToken, new
        {
            fullName = "Gestor Pizzaria",
            email = "gestor@fornonobre.com.br",
            password = "Pizza@2026",
            role = 1
        }));
        await _client.SendAsync(AuthReq(HttpMethod.Post, $"/api/v1/master/establishments/{burgerId}/admin-user", masterToken, new
        {
            fullName = "Gestor Burger",
            email = "gestor@burgerbrass.com.br",
            password = "Burger@2026",
            role = 1
        }));

        var pizzaToken = (await (await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "forno-nobre-e2e",
            email = "gestor@fornonobre.com.br",
            password = "Pizza@2026"
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var burgerToken = (await (await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "burger-brass-e2e",
            email = "gestor@burgerbrass.com.br",
            password = "Burger@2026"
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        // 2. Aplicar templates em cada loja + verificar isolamento multi-tenant
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/catalog/apply-segment-template", pizzaToken, new { segmentCode = "pizzaria" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/catalog/apply-segment-template", burgerToken, new { segmentCode = "hamburgueria" }))).StatusCode);

        var pizzaPublicMenu = await (await _client.GetAsync("/api/v1/public/store/forno-nobre-e2e/menu")).Content.ReadAsStringAsync();
        Assert.Contains("Pizza", pizzaPublicMenu);
        Assert.DoesNotContain("Burger Artesanal", pizzaPublicMenu);
        var menuRoot = JsonDocument.Parse(pizzaPublicMenu).RootElement;
        Assert.False(menuRoot.GetProperty("usesPitakoBranding").GetBoolean());
        Assert.DoesNotContain("PITAKO", menuRoot.GetProperty("storeTradeName").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // 3. Configurar Insumo com Perda (10%) + Saldo + Compra + Vincular em Modificador (Granola/Calabresa 100g)
        var supplyRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/inventory/supplies", pizzaToken, new
        {
            name = "Mussarela Especial",
            purchasePrice = 40.00m,
            purchaseQuantity = 1m,
            purchaseUnitCode = "kg",
            contentQuantityPerPackage = 1000m,
            contentUnitCode = "g",
            defaultLossPercent = 10m,
            controlUnitCode = "g",
            minimumStockQuantity = 500m,
            allowNegativeStock = false
        }));
        Assert.Equal(HttpStatusCode.Created, supplyRes.StatusCode);
        var supplyId = (await supplyRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var purchaseRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/inventory/purchases", pizzaToken, new
        {
            supplierName = "Laticínios Sul",
            invoiceNumber = "NF-9001",
            lines = new[] { new { supplyItemId = supplyId, purchasedQuantity = 2m, purchaseUnitCode = "kg", unitPriceAmount = 40m } },
            updateSupplyPrice = false
        }));
        Assert.Equal(HttpStatusCode.Created, purchaseRes.StatusCode);

        // Recuperar pizza, variações e grupos
        var catJson = await (await _client.SendAsync(AuthReq(HttpMethod.Get, "/api/v1/catalog/products", pizzaToken))).Content.ReadFromJsonAsync<JsonElement>();
        var pizzaProd = catJson.GetProperty("products")[0];
        var pizzaId = pizzaProd.GetProperty("id").GetGuid();
        var grandeVarId = pizzaProd.GetProperty("variations").EnumerateArray().First(v => v.GetProperty("variationName").GetString()!.Contains("Grande")).GetProperty("id").GetGuid();
        var brotoVarId = pizzaProd.GetProperty("variations").EnumerateArray().First(v => v.GetProperty("variationName").GetString()!.Contains("Broto")).GetProperty("id").GetGuid();

        var groupsArr = catJson.GetProperty("modifierGroups").EnumerateArray().ToList();
        var flavorGroup = groupsArr.First(g => g.GetProperty("selectionKind").GetInt32() == 6);
        var flavorGroupId = flavorGroup.GetProperty("id").GetGuid();
        var flavorOpt0 = flavorGroup.GetProperty("options").EnumerateArray().First();
        var flavorOpt0Id = flavorOpt0.GetProperty("id").GetGuid();

        // Vincular mussarela (100g + 10% perda = 110g) no sabor escolhido
        var updateOptRes = await _client.SendAsync(AuthReq(HttpMethod.Put, $"/api/v1/catalog/modifier-groups/{flavorGroupId}/options/{flavorOpt0Id}", pizzaToken, new
        {
            input = new
            {
                name = flavorOpt0.GetProperty("name").GetString()!,
                additionalPrice = flavorOpt0.GetProperty("additionalPrice").GetDecimal(),
                maxQuantity = 1,
                displayOrder = 0,
                description = "Mussarela tradicional",
                supplyItemId = supplyId,
                consumptionQuantity = 100m,
                consumptionUnitCode = "g",
                lossPercent = 10m
            },
            isAvailable = true
        }));
        Assert.Equal(HttpStatusCode.OK, updateOptRes.StatusCode);

        // Cadastrar cupom de 10% OFF
        var couponRes = await _client.SendAsync(AuthReq(HttpMethod.Post, "/api/v1/coupons", pizzaToken, new
        {
            code = "PIZZA10",
            discountType = 1,
            discountValue = 10m,
            validFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
            validUntilUtc = DateTimeOffset.UtcNow.AddDays(10),
            minimumOrderAmount = 20m,
            canCombineWithPromotions = true
        }));
        Assert.Equal(HttpStatusCode.Created, couponRes.StatusCode);

        // 4. Fluxo do Cliente no Cardápio Digital: Quote -> Add Cart -> Edição In-Place -> Cupom -> Frete -> Checkout Idempotente
        var sessionKey = "sess-e2e-pizzaria-01";
        var addRes = await _client.PostAsJsonAsync($"/api/v1/public/store/forno-nobre-e2e/cart/{sessionKey}/items", new
        {
            kind = "catalog",
            productId = pizzaId,
            variationId = brotoVarId,
            modifiers = new[] { new { modifierGroupId = flavorGroupId, modifierOptionId = flavorOpt0Id, quantity = 1 } },
            quantity = 1,
            notes = "Bem assada"
        });
        var addBody = await addRes.Content.ReadAsStringAsync();
        Assert.True(addRes.StatusCode == HttpStatusCode.OK, $"addRes failed ({addRes.StatusCode}): {addBody}");
        var cartItemId = JsonDocument.Parse(addBody).RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        // Editar o item NO PRÓPRIO CARRINHO sem remover (mudar para Grande e qty=2)
        var editCartRes = await _client.PutAsJsonAsync($"/api/v1/public/store/forno-nobre-e2e/cart/{sessionKey}/items/{cartItemId}", new
        {
            quantity = 2,
            variationId = grandeVarId,
            modifiers = new[] { new { modifierGroupId = flavorGroupId, modifierOptionId = flavorOpt0Id, quantity = 1 } },
            customerNotes = "Sem orégano"
        });
        Assert.Equal(HttpStatusCode.OK, editCartRes.StatusCode);
        var editedCartJson = await editCartRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, editedCartJson.GetProperty("items")[0].GetProperty("quantity").GetInt32());

        var applyCouponRes = await _client.PostAsJsonAsync($"/api/v1/public/store/forno-nobre-e2e/cart/{sessionKey}/coupon", new
        {
            couponCode = "PIZZA10",
            customerIdentifier = "16999887766"
        });
        Assert.Equal(HttpStatusCode.OK, applyCouponRes.StatusCode);
        var discountedCart = await applyCouponRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(discountedCart.GetProperty("itemsDiscount").GetDecimal() > 0m);

        var idempKey = $"ord-{Guid.NewGuid():N}";
        var checkoutRes = await _client.PostAsJsonAsync("/api/v1/public/store/forno-nobre-e2e/checkout", new
        {
            customerName = "Carlos Silva",
            customerPhone = "16999887766",
            fulfillmentMode = 1,
            deliveryAddress = "Rua Chile, 500 - Centro",
            cartSessionKey = sessionKey,
            idempotencyKey = idempKey,
            paymentMethod = "PIX",
            lgpdConsentAccepted = true
        });
        Assert.Equal(HttpStatusCode.Created, checkoutRes.StatusCode);
        var orderJson = await checkoutRes.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = orderJson.GetProperty("id").GetGuid();
        var orderNumber = orderJson.GetProperty("orderNumber").GetString()!;

        // 5. Verificar baixa de estoque COM perda (2 pizzas * 100g * 1.10 = 220g -> saldo cai de 2000g para 1780g)
        var stockAfterOrder = await (await _client.SendAsync(AuthReq(HttpMethod.Get, "/api/v1/inventory/stock", pizzaToken))).Content.ReadFromJsonAsync<JsonElement>();
        var mussarelaAfter = stockAfterOrder.EnumerateArray().First(s => s.GetProperty("supplyItemId").GetGuid() == supplyId).GetProperty("quantityOnHand").GetDecimal();
        Assert.Equal(1780m, mussarelaAfter);

        // Retry idempotente do mesmo checkout NÃO pode baixar estoque em duplicidade!
        var retryRes = await _client.PostAsJsonAsync("/api/v1/public/store/forno-nobre-e2e/checkout", new
        {
            customerName = "Carlos Silva",
            customerPhone = "16999887766",
            fulfillmentMode = 1,
            deliveryAddress = "Rua Chile, 500 - Centro",
            cartSessionKey = sessionKey,
            idempotencyKey = idempKey,
            paymentMethod = "PIX",
            lgpdConsentAccepted = true
        });
        Assert.Equal(HttpStatusCode.Created, retryRes.StatusCode);
        var stockAfterRetry = await (await _client.SendAsync(AuthReq(HttpMethod.Get, "/api/v1/inventory/stock", pizzaToken))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1780m, stockAfterRetry.EnumerateArray().First(s => s.GetProperty("supplyItemId").GetGuid() == supplyId).GetProperty("quantityOnHand").GetDecimal());

        // 6. Acompanhamento público LGPD: sem telefone não expõe endereço; com telefone expõe
        var trackAnon = await (await _client.GetAsync($"/api/v1/public/store/forno-nobre-e2e/orders/{Uri.EscapeDataString(orderNumber)}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, trackAnon.GetProperty("deliveryAddressSummary").ValueKind);

        var trackVerified = await (await _client.GetAsync($"/api/v1/public/store/forno-nobre-e2e/orders/{Uri.EscapeDataString(orderNumber)}?phone=7766")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Rua Chile", trackVerified.GetProperty("deliveryAddressSummary").GetString()!);

        // 7. Cancelar pedido pelo painel e confirmar estorno integral dos 220g de estoque (volta para 2000g)
        var cancelRes = await _client.SendAsync(AuthReq(HttpMethod.Post, $"/api/v1/orders/{orderId}/status", pizzaToken, new
        {
            nextStatus = 7,
            reason = "Cliente desistiu"
        }));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        var stockAfterCancel = await (await _client.SendAsync(AuthReq(HttpMethod.Get, "/api/v1/inventory/stock", pizzaToken))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2000m, stockAfterCancel.EnumerateArray().First(s => s.GetProperty("supplyItemId").GetGuid() == supplyId).GetProperty("quantityOnHand").GetDecimal());
    }
}
