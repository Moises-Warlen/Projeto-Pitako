using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pitako.Api;
using Pitako.Api.Bootstrap;
using Pitako.Api.Composition;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.SecurityTests;

public sealed class PitakoApiSecurityHardeningTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication? _app;
    private HttpClient? _client;
    private Guid _tenantBId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _app = PitakoApiHostBuilder.BuildWebApplication(
            ["--environment", "Development"],
            sharedSqliteConnection: _connection,
            listenUrl: "http://127.0.0.1:0");

        await _app.Services.EnsureDatabaseInitializedAsync(useSqlite: true);
        await PitakoDevDemoSeeder.SeedIfNeededAsync(_app.Services);

        // Cria Tenant B com plano restrito (SEM módulo Finance) para testar entitlement e cross-tenant HTTP.
        using (var scope = _app.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContextSetter>().SetMasterScope();
            var userRepo = sp.GetRequiredService<IUserRepository>();
            var master = (await userRepo.FindByEmailAsync(UserScope.Saas, null, "MASTER@PITAKO.COM.BR"))!;
            var planSvc = sp.GetRequiredService<MasterPlanAndSubscriptionService>();
            var prov = sp.GetRequiredService<TenantProvisioningService>();
            var userAuth = sp.GetRequiredService<UserAuthorizationService>();

            var basicQuotas = new PlanQuotaConfiguration(
                QuotaLimit.Limited(1).Value, QuotaLimit.Limited(1).Value, QuotaLimit.Limited(1).Value,
                QuotaLimit.Limited(5).Value, QuotaLimit.Limited(2).Value, QuotaLimit.Limited(2).Value, QuotaLimit.Limited(200).Value);
            var basicPlan = (await planSvc.CreatePlanAsync(master, new CreatePlanCommand(
                "basico-sem-finance", "Básico", "Sem módulo financeiro", 99m, "BRL", BillingCycle.Monthly, 7, basicQuotas,
                [SystemModules.Tenants, SystemModules.Catalog, SystemModules.Security]))).Value;

            var subB = (await planSvc.CreateCustomerSubscriptionAsync(
                master, "Loja B Ltda", "98765432000198", "lojab@example.com", "11988887777", basicPlan.Id, false)).Value;

            var tenantB = (await prov.CreateEstablishmentAsync(master, new CreateEstablishmentCommand(
                subB.Customer.Id, "Loja B Ltda", "Sorveteria B", "loja-b", "98765432000198", BusinessSegmentCatalog.Sorveteria))).Value.Tenant;
            _tenantBId = tenantB.Id;

            await userAuth.CreateEstablishmentUserAsync(master, _tenantBId, "Admin B", "admin@lojab.com", "StoreB@123", EstablishmentRole.Administrator);
        }

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
    public async Task HttpSecurityHeaders_Unauth_ForgedTenantHeader_Rbac_Entitlement_AndPciReject_ShouldEnforceStrictly()
    {
        // 1. Sem token -> 401 + Security Headers presentes
        var unauthRes = await _client!.GetAsync("/api/v1/finance/dre");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
        Assert.Equal("nosniff", unauthRes.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", unauthRes.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", unauthRes.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(unauthRes.Headers.Contains("X-Correlation-ID"));

        // 2. Login Admin da Loja A (acai-demo)
        var loginA = await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "acai-demo",
            email = "admin@tropicalexpress.com.br",
            password = "Store@12345"
        });
        var tokenA = (await loginA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        // 3. Usuário de estabelecimento tentando Painel Master -> 403
        using var masterProbe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/master/plans");
        masterProbe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var masterProbeRes = await _client.SendAsync(masterProbe);
        Assert.Equal(HttpStatusCode.Forbidden, masterProbeRes.StatusCode);

        // 4. Cabeçalho X-Tenant-Id forjado apontando para Loja B -> 403 bloqueado
        using var forgedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/products");
        forgedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        forgedReq.Headers.Add("X-Tenant-Id", _tenantBId.ToString());
        var forgedRes = await _client.SendAsync(forgedReq);
        Assert.Equal(HttpStatusCode.Forbidden, forgedRes.StatusCode);

        // 5. Caixa (menor privilégio) tentando consultar DRE ou configurar gateway -> 403
        var loginCashier = await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "acai-demo",
            email = "caixa@tropicalexpress.com.br",
            password = "Caixa@12345"
        });
        var cashierToken = (await loginCashier.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        using var cashierDreReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/finance/dre");
        cashierDreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierDreRes = await _client.SendAsync(cashierDreReq);
        Assert.Equal(HttpStatusCode.Forbidden, cashierDreRes.StatusCode);

        // 6. Admin Loja B (plano básico sem módulo Finance) acessando /api/v1/finance/dre -> 403 Plan.ModuleNotIncluded
        var loginB = await _client.PostAsJsonAsync("/api/v1/auth/login-store", new
        {
            storeSlug = "loja-b",
            email = "admin@lojab.com",
            password = "StoreB@123"
        });
        var tokenB = (await loginB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        using var moduleBlockedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/finance/dre");
        moduleBlockedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var moduleBlockedRes = await _client.SendAsync(moduleBlockedReq);
        Assert.Equal(HttpStatusCode.Forbidden, moduleBlockedRes.StatusCode);
        var blockedJson = await moduleBlockedRes.Content.ReadAsStringAsync();
        Assert.Contains("Plan.ModuleNotIncluded", blockedJson);

        // 7. Tentativa PCI inválida enviando número completo de cartão (PAN) -> 400 ProblemDetails
        using var gwReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/gateways");
        gwReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        gwReq.Content = JsonContent.Create(new { provider = 1, merchantAccountCode = "ST-01", apiSecret = "sk-test-123", mdrPercent = 2.4m });
        var gwRes = await _client.SendAsync(gwReq);
        Assert.Equal(HttpStatusCode.Created, gwRes.StatusCode);
        var gwId = (await gwRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var panReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/finance/gateways/{gwId}/charges");
        panReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        panReq.Content = JsonContent.Create(new
        {
            orderOrSaleReference = "ORD-99",
            grossAmount = 55.00m,
            cardBrandOrPix = "VISA",
            cardLast4Digits = "8901",
            gatewayTokenReference = "4532 0123 4567 8901"
        });
        var panRes = await _client.SendAsync(panReq);
        Assert.Equal(HttpStatusCode.BadRequest, panRes.StatusCode);
        var panBody = await panRes.Content.ReadAsStringAsync();
        Assert.Contains("Finance.RawCardDataForbidden", panBody);
    }
}
