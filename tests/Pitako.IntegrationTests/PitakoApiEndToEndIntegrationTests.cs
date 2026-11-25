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
    }
}
