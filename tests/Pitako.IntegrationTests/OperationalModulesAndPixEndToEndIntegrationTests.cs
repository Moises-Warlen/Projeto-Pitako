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
    }
}
