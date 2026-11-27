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

public sealed class OperationalModulesAndPixEndToEndIntegrationTests : IAsyncLifetime
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
    public async Task Totem_DiningQr_FinancePci_Pdv_OrdersPix_AndDeliveryOptimize_ShouldWorkEndToEnd()
    {
        // Initialize client non-null shorthand
        var client = _client!;

        // Login store operator
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "acai-demo",
            email = "admin@tropicalexpress.com.br",
            password = "Store@12345"
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var storeToken = (await loginRes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken")
            .GetString()!;

        // 1. Totem Self-Service Flow (PIX)
        var publicMenuRes = await client.GetAsync("/api/v1/public/store/acai-demo/menu");
        Assert.Equal(HttpStatusCode.OK, publicMenuRes.StatusCode);
        var publicMenuJson = await publicMenuRes.Content.ReadFromJsonAsync<JsonElement>();
        var firstProductId = publicMenuJson.GetProperty("products")[0].GetProperty("id").GetGuid();

        var startTotem = await client.PostAsJsonAsync("/api/v1/public/totem/acai-demo/start", new
        {
            choice = 1,
            customerName = "Cliente Totem Teste"
        });
        Assert.Equal(HttpStatusCode.Created, startTotem.StatusCode);
        var totemSessionToken = (await startTotem.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionToken")
            .GetString()!;

        var addTotemItem = await client.PostAsJsonAsync(
            $"/api/v1/public/totem/acai-demo/sessions/{totemSessionToken}/items",
            new
            {
                kind = "catalog",
                productId = firstProductId,
                quantity = 2,
                notes = "Sem granola"
            });
        Assert.Equal(HttpStatusCode.OK, addTotemItem.StatusCode);

        var checkoutTotem = await client.PostAsJsonAsync(
            $"/api/v1/public/totem/acai-demo/sessions/{totemSessionToken}/checkout",
            new
            {
                paymentChoice = 2,
                idempotencyKey = $"totem-test-{Guid.NewGuid():N}"
            });
        var checkoutBody = await checkoutTotem.Content.ReadAsStringAsync();
        Assert.True(checkoutTotem.IsSuccessStatusCode, $"Checkout failed ({checkoutTotem.StatusCode}): {checkoutBody}");
        var totemCheckoutJson = JsonDocument.Parse(checkoutBody).RootElement;
        var totemOrderNumber = totemCheckoutJson.GetProperty("dispatchedOrderNumber").GetString()!;
        Assert.StartsWith("T-", totemCheckoutJson.GetProperty("pickupCallCode").GetString());
        Assert.Contains("BR.GOV.BCB.PIX", totemCheckoutJson.GetProperty("pixBrCode").GetString(), StringComparison.OrdinalIgnoreCase);

        // Verify public PIX confirmation on Totem order
        var verifyPixRes = await client.PostAsJsonAsync(
            $"/api/v1/public/store/acai-demo/orders/{Uri.EscapeDataString(totemOrderNumber)}/verify-pix",
            new { });
        Assert.Equal(HttpStatusCode.OK, verifyPixRes.StatusCode);
        var verifyPixJson = await verifyPixRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verifyPixJson.GetProperty("isPaymentConfirmed").GetBoolean());

        // 2. Dining Hall Overview + Tab Split + Pay
        using var overviewReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dining/overview");
        overviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var overviewRes = await client.SendAsync(overviewReq);
        Assert.Equal(HttpStatusCode.OK, overviewRes.StatusCode);
        var overviewJson = await overviewRes.Content.ReadFromJsonAsync<JsonElement>();
        var firstTable = overviewJson.GetProperty("tables")[0];
        var tableId = firstTable.GetProperty("id").GetGuid();
        var qrToken = firstTable.GetProperty("qrCodeToken").GetString()!;

        using var openTabReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dining/tabs");
        openTabReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        openTabReq.Content = JsonContent.Create(new
        {
            customerName = "Mesa Teste Split",
            guestCount = 3,
            diningTableId = tableId,
            serviceChargePercent = 10m
        });
        var openTabRes = await client.SendAsync(openTabReq);
        Assert.Equal(HttpStatusCode.Created, openTabRes.StatusCode);
        var tabId = (await openTabRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var qrAddRes = await client.PostAsJsonAsync($"/api/v1/public/qr/acai-demo/{qrToken}/items", new
        {
            kind = "catalog",
            productId = firstProductId,
            quantity = 1,
            notes = "QR Test"
        });
        Assert.Equal(HttpStatusCode.OK, qrAddRes.StatusCode);
        var qrAddJson = await qrAddRes.Content.ReadFromJsonAsync<JsonElement>();
        var expectedTabTotal = qrAddJson.GetProperty("tab").GetProperty("totalAmount").GetDecimal();
        Assert.True(expectedTabTotal > 0m);

        using var splitReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/dining/tabs/{tabId}/split/3");
        splitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var splitRes = await client.SendAsync(splitReq);
        Assert.Equal(HttpStatusCode.OK, splitRes.StatusCode);
        var splitArr = (await splitRes.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(3, splitArr.Count);
        Assert.Equal(expectedTabTotal, splitArr.Sum(s => s.GetProperty("amount").GetDecimal()));

        // 3. Finance Entries + PCI-Safe Guard
        using var createEntryReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/entries");
        createEntryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        createEntryReq.Content = JsonContent.Create(new
        {
            kind = 2,
            dreCategory = 3,
            counterpartyName = "Fornecedor Polpa",
            description = "Polpa 10kg",
            amount = 180.00m,
            dueDateUtc = DateTimeOffset.UtcNow.AddDays(3)
        });
        var entryRes = await client.SendAsync(createEntryReq);
        Assert.Equal(HttpStatusCode.Created, entryRes.StatusCode);

        using var listEntriesReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/finance/entries");
        listEntriesReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var entriesListRes = await client.SendAsync(listEntriesReq);
        Assert.Equal(HttpStatusCode.OK, entriesListRes.StatusCode);
        Assert.Contains((await entriesListRes.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
            e => e.GetProperty("counterpartyName").GetString() == "Fornecedor Polpa");

        using var gwReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/gateways");
        gwReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        gwReq.Content = JsonContent.Create(new
        {
            provider = 7,
            merchantAccountCode = "MERCH-ACAI-01",
            apiSecret = "super-secret-key",
            mdrPercent = 0.99m,
            settlementDays = 1
        });
        var gwRes = await client.SendAsync(gwReq);
        Assert.Equal(HttpStatusCode.Created, gwRes.StatusCode);
        var gwId = (await gwRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var badPciReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/finance/gateways/{gwId}/charges");
        badPciReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        badPciReq.Content = JsonContent.Create(new
        {
            orderOrSaleReference = "PED-PAN-TEST",
            grossAmount = 25.00m,
            cardBrandOrPix = "VISA",
            cardLast4Digits = "1111",
            gatewayTokenReference = "4111111111111111"
        });
        var badPciRes = await client.SendAsync(badPciReq);
        Assert.False(badPciRes.IsSuccessStatusCode);

        // 4. PDV Open + Sale
        using var openPdvReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pdv/sessions/open");
        openPdvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        openPdvReq.Content = JsonContent.Create(new { terminalCode = "PDV-E2E", openingBalance = 150.00m });
        var openPdvRes = await client.SendAsync(openPdvReq);
        Assert.Equal(HttpStatusCode.Created, openPdvRes.StatusCode);
        var pdvSessionId = (await openPdvRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var pdvSaleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pdv/sales");
        pdvSaleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        pdvSaleReq.Content = JsonContent.Create(new
        {
            cashRegisterSessionId = pdvSessionId,
            grossAmount = 42.00m,
            discountAmount = 0m,
            payments = new[] { new { method = 2, allocatedAmount = 42.00m, tenderedAmount = 42.00m } }
        });
        var pdvSaleRes = await client.SendAsync(pdvSaleReq);
        Assert.Equal(HttpStatusCode.Created, pdvSaleRes.StatusCode);

        // 5. Orders All + Route Optimize
        using var allOrdersReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orders/all");
        allOrdersReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var allOrdersRes = await client.SendAsync(allOrdersReq);
        Assert.Equal(HttpStatusCode.OK, allOrdersRes.StatusCode);
        var ordersArray = (await allOrdersRes.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Contains(ordersArray, o => o.GetProperty("orderNumber").GetString() == totemOrderNumber && o.GetProperty("isPaymentConfirmed").GetBoolean());

        using var couriersReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/delivery/couriers");
        couriersReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var couriersRes = await client.SendAsync(couriersReq);
        Assert.Equal(HttpStatusCode.OK, couriersRes.StatusCode);
        var firstCourierId = (await couriersRes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("couriers")[0]
            .GetProperty("id")
            .GetGuid();

        var totemOrderId = ordersArray.First(o => o.GetProperty("orderNumber").GetString() == totemOrderNumber).GetProperty("id").GetGuid();

        using var optimizeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/delivery/dispatch-runs/optimize");
        optimizeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        optimizeReq.Content = JsonContent.Create(new
        {
            courierId = firstCourierId,
            originLatitude = -20.5386,
            originLongitude = -47.4008,
            stops = new[]
            {
                new
                {
                    orderId = totemOrderId,
                    orderNumber = totemOrderNumber,
                    customerName = "Cliente Rota",
                    destinationAddress = "Rua Major Nicácio 100",
                    destinationLatitude = -20.5420,
                    destinationLongitude = -47.4050,
                    deliveryFee = 6.00m
                }
            },
            notes = "Rota E2E"
        });
        var optimizeRes = await client.SendAsync(optimizeReq);
        Assert.Equal(HttpStatusCode.Created, optimizeRes.StatusCode);
        var optJson = await optimizeRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, optJson.GetProperty("stops")[0].GetProperty("sequenceNumber").GetInt32());

        // 6. Per-Store Marketplaces (iFood / 99Food / Rappi) + Multi-Tenant Isolation
        using var mpOverviewReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/marketplaces/overview");
        mpOverviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var mpOverviewRes = await client.SendAsync(mpOverviewReq);
        Assert.Equal(HttpStatusCode.OK, mpOverviewRes.StatusCode);
        var mpJson = await mpOverviewRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, mpJson.GetProperty("capabilities").GetArrayLength());
        var food99Binding = mpJson.GetProperty("bindings").EnumerateArray().First(b => b.GetProperty("providerName").GetString() == "Food99");
        var food99BindingId = food99Binding.GetProperty("id").GetGuid();

        using var sim99Req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/marketplaces/bindings/{food99BindingId}/simulate-order");
        sim99Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        sim99Req.Content = JsonContent.Create(new { customerName = "Cliente 99Food E2E" });
        var sim99Res = await client.SendAsync(sim99Req);
        Assert.Equal(HttpStatusCode.Created, sim99Res.StatusCode);
        var sim99Json = await sim99Res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("99-", sim99Json.GetProperty("orderNumber").GetString());

        // Provision a 2nd independent store and confirm ZERO cross-tenant marketplace leakage
        var masterLogin = await client.PostAsJsonAsync("/api/v1/auth/login-saas", new
        {
            email = "master@pitako.com.br",
            password = "Master@12345"
        });
        Assert.Equal(HttpStatusCode.OK, masterLogin.StatusCode);
        var masterToken = (await masterLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var plansReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/master/plans");
        plansReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        var unlimitedPlanId = (await (await client.SendAsync(plansReq)).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();

        using var custReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/master/customers-subscriptions");
        custReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        custReq.Content = JsonContent.Create(new
        {
            customerName = "Segunda Loja Holding",
            documentNumber = "11444777000161",
            billingEmail = "dono2@pitako.com.br",
            phone = "16998887766",
            planId = unlimitedPlanId,
            startWithTrial = false
        });
        var custRes = await client.SendAsync(custReq);
        Assert.Equal(HttpStatusCode.Created, custRes.StatusCode);
        var cust2Id = (await custRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();

        using var store2Req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/store/establishments");
        store2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        store2Req.Content = JsonContent.Create(new
        {
            customerAccountId = cust2Id,
            legalName = "Explosão Açaí Unidade 2 LTDA",
            tradeName = "Explosão Açaí Filial",
            slug = "explosao-filial",
            cnpj = "22.333.444/0001-81",
            segmentCode = "acaiteria"
        });
        var store2Res = await client.SendAsync(store2Req);
        Assert.Equal(HttpStatusCode.Created, store2Res.StatusCode);
        var store2Id = (await store2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenant").GetProperty("id").GetGuid();

        using var admin2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/master/establishments/{store2Id}/admin-user");
        admin2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        admin2Req.Content = JsonContent.Create(new
        {
            fullName = "Gerente Filial",
            email = "gerente@explosaofilial.com.br",
            password = "Loja2@12345",
            role = 1
        });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(admin2Req)).StatusCode);

        var login2Res = await client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "explosao-filial",
            email = "gerente@explosaofilial.com.br",
            password = "Loja2@12345"
        });
        Assert.Equal(HttpStatusCode.OK, login2Res.StatusCode);
        var token2 = (await login2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var mp2Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/marketplaces/overview");
        mp2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var mp2Res = await client.SendAsync(mp2Req);
        Assert.Equal(HttpStatusCode.OK, mp2Res.StatusCode);
        var mp2Json = await mp2Res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("explosao-filial", mp2Json.GetProperty("storeSlug").GetString());
        Assert.Equal(0, mp2Json.GetProperty("accounts").GetArrayLength());
        Assert.Equal(0, mp2Json.GetProperty("bindings").GetArrayLength());

        // 7. Auto-Provisionamento Completo na Loja 2 ("explosao-filial") + Novos Endpoints Operacionais
        var pubStore2 = await client.GetAsync("/api/v1/public/store/explosao-filial/");
        Assert.Equal(HttpStatusCode.OK, pubStore2.StatusCode);
        var pubStore2Json = await pubStore2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("data:image/svg+xml", pubStore2Json.GetProperty("branding").GetProperty("logoUrl").GetString());

        using var sheets2Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/technical-sheets/summary");
        sheets2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var sheets2Res = await client.SendAsync(sheets2Req);
        Assert.Equal(HttpStatusCode.OK, sheets2Res.StatusCode);
        Assert.True((await sheets2Res.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength() >= 1);

        using var printers2Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/printing/printers");
        printers2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var printers2Res = await client.SendAsync(printers2Req);
        Assert.Equal(HttpStatusCode.OK, printers2Res.StatusCode);
        var printersArr = (await printers2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printers");
        Assert.True(printersArr.GetArrayLength() >= 2);
        var firstPrinterId = printersArr[0].GetProperty("id").GetGuid();

        using var printTestReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/printing/printers/{firstPrinterId}/test");
        printTestReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var printTestRes = await client.SendAsync(printTestReq);
        Assert.Equal(HttpStatusCode.OK, printTestRes.StatusCode);
        var printPreview = (await printTestRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("previewTicketText").GetString()!;
        Assert.Contains("EXPLOS", printPreview.ToUpperInvariant());
        Assert.DoesNotContain("PITAKO", printPreview.ToUpperInvariant());

        using var coupons2Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/coupons");
        coupons2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var coupons2Res = await client.SendAsync(coupons2Req);
        Assert.Equal(HttpStatusCode.OK, coupons2Res.StatusCode);
        Assert.Contains((await coupons2Res.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(), c => c.GetProperty("code").GetString() == "PRIMEIRACOMPRA");

        var pubChatPost = await client.PostAsJsonAsync("/api/v1/public/store/explosao-filial/chat", new
        {
            customerName = "Maria Loja 2",
            customerPhone = "16991234567",
            message = "Boa noite, tem copo de 500ml?"
        });
        Assert.Equal(HttpStatusCode.OK, pubChatPost.StatusCode);
        var pubChatJson = await pubChatPost.Content.ReadFromJsonAsync<JsonElement>();
        var convId2 = pubChatJson.GetProperty("conversationId").GetGuid();
        var chatMessages2 = pubChatJson.GetProperty("messages").EnumerateArray().ToList();
        Assert.True(chatMessages2.Count >= 2, $"Expected automatic grounded reply in chat, got {chatMessages2.Count} messages");
        Assert.Contains(chatMessages2, m => m.GetProperty("direction").GetString()!.StartsWith("Outbound", StringComparison.OrdinalIgnoreCase));

        using var reply2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/whatsapp/conversations/{convId2}/reply");
        reply2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        reply2Req.Content = JsonContent.Create(new { messageText = "Sim! Copo 500ml disponível no cardápio." });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reply2Req)).StatusCode);

        using var waDashReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/whatsapp/dashboard");
        waDashReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var waDashRes = await client.SendAsync(waDashReq);
        Assert.Equal(HttpStatusCode.OK, waDashRes.StatusCode);
        Assert.True((await waDashRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conversations").GetArrayLength() >= 1);

        using var execRepReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/executive?days=7");
        execRepReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(execRepReq)).StatusCode);

        using var secLogsReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/audit-logs");
        secLogsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(secLogsReq)).StatusCode);

        using var opsSetReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/store/operational-settings");
        opsSetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var opsSetRes = await client.SendAsync(opsSetReq);
        Assert.Equal(HttpStatusCode.OK, opsSetRes.StatusCode);
        var opsSetJson = await opsSetRes.Content.ReadFromJsonAsync<JsonElement>();
        var scheduleDays = opsSetJson.GetProperty("schedule").EnumerateArray().Select(s => s.GetProperty("dayOfWeek").GetInt32()).ToArray();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], scheduleDays);

        using var kitReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/catalog/import-spreadsheet-kit");
        kitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        kitReq.Content = JsonContent.Create(new { segmentCode = "pizzaria" });
        var kitRes = await client.SendAsync(kitReq);
        Assert.Equal(HttpStatusCode.OK, kitRes.StatusCode);

        // 8. Media PC Upload, Segment Artwork, Full Product Config, Quick Tech Sheet & QR PIX Direct Settlement
        var segArtRes = await client.GetAsync("/api/v1/public/segment-art/pizzaria/mussarela");
        Assert.Equal(HttpStatusCode.OK, segArtRes.StatusCode);
        Assert.Equal("image/svg+xml", segArtRes.Content.Headers.ContentType?.MediaType);

        // 1x1 PNG pixel base64
        const string tinyPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/catalog/upload-image");
        uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        uploadReq.Content = JsonContent.Create(new { base64DataUrl = tinyPng, fileName = "pizza.png" });
        var uploadRes = await client.SendAsync(uploadReq);
        Assert.Equal(HttpStatusCode.OK, uploadRes.StatusCode);
        var uploadedUrl = (await uploadRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        Assert.StartsWith("/api/v1/public/media/", uploadedUrl);

        var mediaGetRes = await client.GetAsync(uploadedUrl);
        Assert.Equal(HttpStatusCode.OK, mediaGetRes.StatusCode);
        Assert.Equal("image/png", mediaGetRes.Content.Headers.ContentType?.MediaType);

        var menu2Res = await client.GetAsync("/api/v1/public/store/explosao-filial/menu");
        Assert.Equal(HttpStatusCode.OK, menu2Res.StatusCode);
        var prods2Arr = (await menu2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("products").EnumerateArray().ToList();
        Assert.NotEmpty(prods2Arr);
        var prod2 = prods2Arr[0];
        var prod2Id = prod2.GetProperty("id").GetGuid();
        var cat2Id = prod2.GetProperty("categoryId").GetGuid();

        using var fullCfgReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/products/{prod2Id}/full-config");
        fullCfgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        fullCfgReq.Content = JsonContent.Create(new
        {
            categoryId = cat2Id,
            name = "Pizza Especial Configurada",
            description = "Teste completo",
            basePrice = 49.90m,
            isNew = true,
            isPromotion = true,
            isFeatured = true,
            displayOrder = 1,
            pricingStrategy = 1,
            imageUrl = uploadedUrl,
            attachedGroupIds = Array.Empty<Guid>(),
            sizes = new[]
            {
                new
                {
                    id = (Guid?)null,
                    variationName = "Grande 8 fatias",
                    price = 54.90m,
                    measureAmount = (decimal?)8m,
                    measureUnit = "fatias",
                    maxFlavors = 2,
                    freeAllowance = 3,
                    costAmount = (decimal?)19.50m,
                    allowPaidExtras = true,
                    description = "Custo R$ 19,50"
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(fullCfgReq)).StatusCode);

        using var sheetsAgainReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/technical-sheets/summary");
        sheetsAgainReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var sheet2Id = (await (await client.SendAsync(sheetsAgainReq)).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();

        using var quickSheetReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/inventory/technical-sheets/{sheet2Id}/quick-ingredients");
        quickSheetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        quickSheetReq.Content = JsonContent.Create(new
        {
            name = "Ficha Atualizada Teste",
            yieldQuantity = 1m,
            yieldUnitCode = "un",
            notes = "Editada no teste E2E",
            ingredients = new[]
            {
                new
                {
                    supplyItemId = (Guid?)null,
                    newSupplyName = "Catupiry Original Teste",
                    quantity = 80m,
                    unitCode = "g",
                    unitPriceAmount = (decimal?)0.06m,
                    lossPercent = 0m
                }
            },
            packagings = Array.Empty<object>()
        });
        var quickSheetRes = await client.SendAsync(quickSheetReq);
        Assert.Equal(HttpStatusCode.OK, quickSheetRes.StatusCode);

        var qrPixGen = await client.PostAsJsonAsync($"/api/v1/public/qr/acai-demo/{qrToken}/pay", new
        {
            paymentMethod = "PIX",
            splitCount = 1,
            confirmImmediatePayment = false
        });
        Assert.Equal(HttpStatusCode.OK, qrPixGen.StatusCode);
        var qrPixJson = await qrPixGen.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("BR.GOV.BCB.PIX", qrPixJson.GetProperty("pixBrCode").GetString(), StringComparison.OrdinalIgnoreCase);

        var qrPixPay = await client.PostAsJsonAsync($"/api/v1/public/qr/acai-demo/{qrToken}/pay", new
        {
            paymentMethod = "PIX",
            splitCount = 1,
            confirmImmediatePayment = true
        });
        Assert.Equal(HttpStatusCode.OK, qrPixPay.StatusCode);
        Assert.True((await qrPixPay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyPaid").GetBoolean());
    }
}
