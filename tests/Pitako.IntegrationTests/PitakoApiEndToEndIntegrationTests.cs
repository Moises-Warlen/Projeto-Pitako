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

public sealed class PitakoApiEndToEndIntegrationTests : IAsyncLifetime
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

        var baseAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(baseAddress) };
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
    public async Task LiveApi_Health_OpenApi_MasterUnlimitedPlan_Storefront_AndFinanceFlow_ShouldWorkEndToEnd()
    {
        // 1. Health & OpenAPI
        var healthRes = await _client!.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        var healthJson = await healthRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", healthJson.GetProperty("status").GetString());
        Assert.Equal("not_configured", healthJson.GetProperty("redis").GetString());

        var openApiRes = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApiRes.StatusCode);

        var normalizedNeon = PitakoServiceCollectionExtensions.NormalizePostgresConnectionString(
            "postgresql://neonuser:p%40ss@ep-test.us-east-2.aws.neon.tech/neondb?sslmode=require");
        Assert.Equal(
            "Host=ep-test.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neonuser;Password=p@ss;SSL Mode=Require;Trust Server Certificate=true;",
            normalizedNeon);

        // 2. Storefront Whitelabel (zero PITAKO branding)
        var storeRes = await _client.GetAsync("/api/v1/public/store/acai-demo/");
        Assert.Equal(HttpStatusCode.OK, storeRes.StatusCode);
        var storeBody = await storeRes.Content.ReadAsStringAsync();
        Assert.Contains("Tropical", storeBody);
        Assert.DoesNotContain("PITAKO", storeBody, StringComparison.OrdinalIgnoreCase);

        var menuRes = await _client.GetAsync("/api/v1/public/store/acai-demo/menu");
        Assert.Equal(HttpStatusCode.OK, menuRes.StatusCode);
        var menuJson = await menuRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(menuJson.GetProperty("products").GetArrayLength() >= 3);
        Assert.True(menuJson.GetProperty("acai").GetProperty("cupSizes").GetArrayLength() >= 2);

        // 3. Master SaaS Auth & Unlimited Plan verification
        var masterLogin = await _client.PostAsJsonAsync("/api/v1/auth/login-saas", new
        {
            email = "master@pitako.com.br",
            password = "Master@12345"
        });
        Assert.Equal(HttpStatusCode.OK, masterLogin.StatusCode);
        var masterAuth = await masterLogin.Content.ReadFromJsonAsync<JsonElement>();
        var masterToken = masterAuth.GetProperty("accessToken").GetString()!;

        using var masterReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/master/plans");
        masterReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        var plansRes = await _client.SendAsync(masterReq);
        Assert.Equal(HttpStatusCode.OK, plansRes.StatusCode);
        var plansJson = await plansRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(plansJson.EnumerateArray(), p =>
            p.GetProperty("code").GetString() == "ilimitado" &&
            p.GetProperty("unlimitedEstablishments").GetBoolean() &&
            p.GetProperty("maxEstablishments").ValueKind == JsonValueKind.Null);

        // 4. Store Auth -> DRE & Report CSV export
        var storeLogin = await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "acai-demo",
            email = "admin@tropicalexpress.com.br",
            password = "Store@12345"
        });
        Assert.Equal(HttpStatusCode.OK, storeLogin.StatusCode);
        var storeAuth = await storeLogin.Content.ReadFromJsonAsync<JsonElement>();
        var storeToken = storeAuth.GetProperty("accessToken").GetString()!;

        using var dreReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/finance/dre");
        dreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        var dreRes = await _client.SendAsync(dreReq);
        Assert.Equal(HttpStatusCode.OK, dreRes.StatusCode);
        var dreJson = await dreRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1250m, dreJson.GetProperty("grossRevenue").GetDecimal());
        Assert.Equal(735m, dreJson.GetProperty("grossProfit").GetDecimal());

        using var reportReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports/export");
        reportReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", storeToken);
        reportReq.Content = JsonContent.Create(new { category = 1, format = 1 });
        var reportRes = await _client.SendAsync(reportReq);
        Assert.Equal(HttpStatusCode.OK, reportRes.StatusCode);
        var csv = await reportRes.Content.ReadAsStringAsync();
        Assert.Contains("Tropical", csv);
        Assert.Contains("11.222.333/0001-81", csv);

        // 5. Master CRUD: Modalidades (segments), Planos, Clientes e Lojas (Create -> Edit -> Delete)
        using var segListReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/master/segments");
        segListReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        var segListJson = await (await _client.SendAsync(segListReq)).Content.ReadFromJsonAsync<JsonElement>();
        var segCodes = segListJson.EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        Assert.Contains("rotisseria", segCodes);
        Assert.Contains("marmitex", segCodes);

        var createSegReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/master/segments");
        createSegReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        createSegReq.Content = JsonContent.Create(new
        {
            code = "temakeria",
            displayName = "Temakeria Express",
            supportsAcaiBuilder = false,
            supportsWeightScale = true,
            supportsTableService = true,
            supportsFractionalItems = false
        });
        var createSegRes = await _client.SendAsync(createSegReq);
        Assert.Equal(HttpStatusCode.OK, createSegRes.StatusCode);

        var editSegReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/master/segments/temakeria");
        editSegReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        editSegReq.Content = JsonContent.Create(new
        {
            code = "temakeria",
            displayName = "Temakeria Premium",
            supportsAcaiBuilder = false,
            supportsWeightScale = false,
            supportsTableService = true,
            supportsFractionalItems = false
        });
        var editSegRes = await _client.SendAsync(editSegReq);
        Assert.Equal(HttpStatusCode.OK, editSegRes.StatusCode);

        var createPlanReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/master/plans");
        createPlanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        createPlanReq.Content = JsonContent.Create(new
        {
            code = "teste-crud",
            name = "Plano Teste CRUD",
            priceAmount = 199.90m,
            description = "Plano temporário",
            trialDays = 7,
            isUnlimited = false,
            maxEstablishments = 2,
            enabledModules = new[] { "Catalog", "Orders", "Finance" }
        });
        var createPlanRes = await _client.SendAsync(createPlanReq);
        Assert.Equal(HttpStatusCode.Created, createPlanRes.StatusCode);
        var createdPlanId = (await createPlanRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var editPlanReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/master/plans/{createdPlanId}");
        editPlanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        editPlanReq.Content = JsonContent.Create(new
        {
            code = "teste-crud",
            name = "Plano Teste CRUD Editado",
            priceAmount = 249.90m,
            description = "Plano editado",
            trialDays = 10,
            isUnlimited = false,
            maxEstablishments = 3,
            enabledModules = new[] { "tenants", "catalog", "orders" },
            isActive = true
        });
        var editPlanRes = await _client.SendAsync(editPlanReq);
        Assert.Equal(HttpStatusCode.OK, editPlanRes.StatusCode);

        var createCustReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/master/customers-subscriptions");
        createCustReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        createCustReq.Content = JsonContent.Create(new
        {
            customerName = "Cliente CRUD Temp",
            documentNumber = "11444777000161",
            billingEmail = "crud@cliente.com.br",
            phone = "11999990000",
            planId = createdPlanId,
            startWithTrial = false
        });
        var createCustRes = await _client.SendAsync(createCustReq);
        Assert.Equal(HttpStatusCode.Created, createCustRes.StatusCode);
        var createdCustId = (await createCustRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();

        var editCustReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/master/customers/{createdCustId}");
        editCustReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        editCustReq.Content = JsonContent.Create(new
        {
            customerName = "Cliente CRUD Editado",
            documentNumber = "11444777000161",
            billingEmail = "crud-edit@cliente.com.br",
            phone = "11988887777",
            status = 1,
            planId = createdPlanId
        });
        var editCustRes = await _client.SendAsync(editCustReq);
        Assert.Equal(HttpStatusCode.OK, editCustRes.StatusCode);

        // Creating a temp store as SaaS Master via /api/v1/store/establishments
        var createStoreReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/store/establishments");
        createStoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        createStoreReq.Content = JsonContent.Create(new
        {
            customerAccountId = createdCustId,
            legalName = "Temakeria CRUD LTDA",
            tradeName = "Temakeria CRUD",
            slug = "temakeria-crud",
            cnpj = "22.333.444/0001-81",
            segmentCode = "temakeria"
        });
        var createStoreRes = await _client.SendAsync(createStoreReq);
        Assert.Equal(HttpStatusCode.Created, createStoreRes.StatusCode);
        var createdStoreId = (await createStoreRes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("tenant")
            .GetProperty("id")
            .GetGuid();

        var editStoreReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/master/establishments/{createdStoreId}");
        editStoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        editStoreReq.Content = JsonContent.Create(new
        {
            legalName = "Temakeria CRUD Editada LTDA",
            tradeName = "Temakeria Express CRUD",
            slug = "temakeria-crud",
            cnpj = "22.333.444/0001-81",
            segmentCode = "temakeria",
            status = 1
        });
        var editStoreRes = await _client.SendAsync(editStoreReq);
        Assert.Equal(HttpStatusCode.OK, editStoreRes.StatusCode);

        // Create Admin User for the new store and verify login via store portal flow
        var createStoreAdminReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/master/establishments/{createdStoreId}/admin-user");
        createStoreAdminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        createStoreAdminReq.Content = JsonContent.Create(new
        {
            fullName = "Dono Temakeria CRUD",
            email = "dono@temakeriacrud.com.br",
            password = "Loja@12345",
            role = 1
        });
        var createStoreAdminRes = await _client.SendAsync(createStoreAdminReq);
        Assert.Equal(HttpStatusCode.Created, createStoreAdminRes.StatusCode);

        var newStoreLogin = await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "temakeria-crud",
            email = "dono@temakeriacrud.com.br",
            password = "Loja@12345"
        });
        Assert.Equal(HttpStatusCode.OK, newStoreLogin.StatusCode);
        var newStoreToken = (await newStoreLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var newStoreBrandReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/store/branding");
        newStoreBrandReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newStoreToken);
        var newStoreBrandRes = await _client.SendAsync(newStoreBrandReq);
        Assert.Equal(HttpStatusCode.OK, newStoreBrandRes.StatusCode);

        // Delete in safe order: Store -> Customer -> Plan -> Segment
        var delStoreReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/master/establishments/{createdStoreId}");
        delStoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(delStoreReq)).StatusCode);

        var delCustReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/master/customers/{createdCustId}");
        delCustReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(delCustReq)).StatusCode);

        var delPlanReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/master/plans/{createdPlanId}");
        delPlanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(delPlanReq)).StatusCode);

        var delSegReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/master/segments/temakeria");
        delSegReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", masterToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(delSegReq)).StatusCode);
    }
}
