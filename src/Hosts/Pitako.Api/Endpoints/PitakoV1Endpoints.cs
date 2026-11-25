using System.Text.Json;
using Pitako.Api.Security;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.DataLifecycle.Application.Services;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Application.Services;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Application.Services;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Application.Services;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Reports.Application.Services;
using Pitako.Modules.Reports.Domain;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Security.Infrastructure.Persistence;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Endpoints;

public static class PitakoV1Endpoints
{
    public static IEndpointRouteBuilder MapPitakoV1Endpoints(this IEndpointRouteBuilder app)
    {
        MapHealthAndOpenApi(app);

        var v1 = app.MapGroup("/api/v1");
        MapAuthEndpoints(v1);
        MapMasterEndpoints(v1);
        MapStoreAndPublicEndpoints(v1);
        MapCatalogAndAcaiEndpoints(v1);
        MapInventoryAndWeighingEndpoints(v1);
        MapOrdersAndPdvEndpoints(v1);
        MapDiningAndSelfServiceEndpoints(v1);
        MapPrintingMarketplaceWhatsAppEndpoints(v1);
        MapFinanceReportsLifecycleOfflineEndpoints(v1);

        return app;
    }

    private static void MapHealthAndOpenApi(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (SecurityDbContext secDb, IConfiguration config, CancellationToken ct) =>
        {
            var canConnectDb = await secDb.Database.CanConnectAsync(ct);
            var redisConn = config.GetConnectionString("Redis");
            var redisStatus = string.IsNullOrWhiteSpace(redisConn) ? "not_configured" : "configured_pending_probe";

            return Results.Ok(new
            {
                service = "Pitako.Api",
                status = canConnectDb ? "Healthy" : "Unhealthy",
                database = canConnectDb ? "reachable" : "unreachable",
                redis = redisStatus,
                timestampUtc = DateTimeOffset.UtcNow
            });
        });

        app.MapGet("/openapi/v1.json", () => Results.Ok(new
        {
            openapi = "3.0.3",
            info = new { title = "PITAKO SaaS Modular Monolith API", version = "1.0.0" },
            servers = new[] { new { url = "/api/v1" } },
            paths = new Dictionary<string, object>
            {
                ["/api/v1/auth/login-saas"] = new { post = new { summary = "Autenticação SaaS Painel Master" } },
                ["/api/v1/auth/login-store"] = new { post = new { summary = "Autenticação de Usuário do Estabelecimento" } },
                ["/api/v1/master/dashboard"] = new { get = new { summary = "Indicadores executivos SaaS" } },
                ["/api/v1/public/store/{slug}"] = new { get = new { summary = "Identidade visual exclusiva da loja" } },
                ["/api/v1/public/store/{slug}/menu"] = new { get = new { summary = "Cardápio digital e Açaíteria da loja" } },
                ["/api/v1/orders/kanban"] = new { get = new { summary = "Painel Kanban em tempo real" } },
                ["/api/v1/finance/dre"] = new { get = new { summary = "Demonstrativo DRE Gerencial" } },
                ["/api/v1/reports/export"] = new { post = new { summary = "Exportação auditada PDF/XLSX/CSV" } },
                ["/api/v1/offline/sync"] = new { post = new { summary = "Sincronização idempotente offline" } }
            }
        }));
    }

    private static void MapAuthEndpoints(RouteGroupBuilder v1)
    {
        var auth = v1.MapGroup("/auth");

        auth.MapPost("/login-saas", async (
            SaasLoginReq req,
            AuthenticationService authSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var res = await authSvc.LoginSaasAsync(
                req.Email,
                req.Password,
                http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString(),
                null,
                ct);
            return ToHttp(res);
        }).RequireRateLimiting("auth-login");

        auth.MapPost("/login-store", async (
            StoreLoginReq req,
            AuthenticationService authSvc,
            ITenantRepository tenantRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(req.StoreSlug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Tenant.NotFound", "Estabelecimento não encontrado."));

            var res = await authSvc.LoginEstablishmentAsync(
                tenant.Id,
                req.Email,
                req.Password,
                http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString(),
                null,
                ct);
            return ToHttp(res);
        }).RequireRateLimiting("auth-login");

        auth.MapGet("/me", async (ApiSecurityGuard guard, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, ct: ct);
            if (err is not null)
                return err;

            return Results.Ok(new
            {
                user!.Id,
                user.FullName,
                user.NormalizedEmail,
                scope = user.Scope.ToString(),
                user.TenantId,
                user.SaasRole,
                user.EstablishmentRole
            });
        });

        auth.MapPost("/password-recovery", async (
            PasswordRecoveryReq req,
            PasswordRecoveryService recoverySvc,
            ITenantRepository tenantRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.StoreSlug))
            {
                var saasRes = await recoverySvc.RequestSaasPasswordResetAsync(req.Email, "/reset-password", "127.0.0.1", ct);
                return Results.Ok(new { accepted = saasRes.Accepted, message = "Se a conta existir, instruções foram emitidas." });
            }

            var tenant = await tenantRepo.FindBySlugAsync(req.StoreSlug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return Results.Ok(new { accepted = true, message = "Se a conta existir, instruções foram emitidas." });

            await recoverySvc.RequestEstablishmentPasswordResetAsync(tenant.Id, req.Email, "/reset-password", "127.0.0.1", ct);
            return Results.Ok(new { accepted = true, message = "Se a conta existir, instruções foram emitidas." });
        });
    }

    private static void MapMasterEndpoints(RouteGroupBuilder v1)
    {
        var master = v1.MapGroup("/master");

        master.MapGet("/dashboard", async (
            ApiSecurityGuard guard,
            MasterOperationsAndDashboardService ops,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return ToHttp(await ops.GetOverviewIndicatorsAsync(user!, ct));
        });

        master.MapGet("/plans", async (
            ApiSecurityGuard guard,
            ICommercialPlanRepository planRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var plans = await planRepo.ListAllAsync(onlyActive: false, ct);
            return Results.Ok(plans.Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                price = p.Price.Amount,
                currency = p.Price.Currency,
                p.BillingCycle,
                p.IsActive,
                unlimitedEstablishments = p.EstablishmentsLimit.IsUnlimited,
                maxEstablishments = p.EstablishmentsLimit.MaxAllowed,
                unlimitedUnits = p.UnitsLimit.IsUnlimited,
                unlimitedUsers = p.UsersLimit.IsUnlimited,
                modules = p.Modules.Select(m => m.ModuleCode).ToList()
            }));
        });

        master.MapPost("/plans", async (
            CreateMasterPlanReq req,
            ApiSecurityGuard guard,
            MasterPlanAndSubscriptionService planSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var quotas = new PlanQuotaConfiguration(
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxEstablishments ?? 1).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxUnits ?? 1).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxMarketplaces ?? 2).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxUsers ?? 10).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxDevices ?? 5).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxSessions ?? 5).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxMonthlyOrders ?? 2000).Value);

            var res = await planSvc.CreatePlanAsync(
                user!,
                new CreatePlanCommand(
                    req.Code,
                    req.Name,
                    req.Description ?? req.Name,
                    req.PriceAmount,
                    "BRL",
                    BillingCycle.Monthly,
                    req.TrialDays,
                    quotas,
                    req.EnabledModules ?? SystemModules.EstablishmentModules),
                ct);
            return ToHttp(res, StatusCodes.Status201Created);
        });

        master.MapPut("/plans/{id:guid}", async (
            Guid id,
            CreateMasterPlanReq req,
            ApiSecurityGuard guard,
            MasterPlanAndSubscriptionService planSvc,
            ICommercialPlanRepository planRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var quotas = new PlanQuotaConfiguration(
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxEstablishments ?? 1).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxUnits ?? 1).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxMarketplaces ?? 2).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxUsers ?? 10).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxDevices ?? 5).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxSessions ?? 5).Value,
                req.IsUnlimited ? QuotaLimit.Unlimited() : QuotaLimit.Limited(req.MaxMonthlyOrders ?? 2000).Value);

            var res = await planSvc.UpdatePlanAsync(
                user!,
                id,
                new CreatePlanCommand(
                    req.Code,
                    req.Name,
                    req.Description ?? req.Name,
                    req.PriceAmount,
                    "BRL",
                    BillingCycle.Monthly,
                    req.TrialDays,
                    quotas,
                    req.EnabledModules ?? SystemModules.EstablishmentModules),
                ct);

            if (res.IsSuccess && req.IsActive.HasValue)
            {
                if (req.IsActive.Value) res.Value.Activate();
                else res.Value.Deactivate();
                await planRepo.SaveChangesAsync(ct);
            }
            return ToHttp(res);
        });

        master.MapDelete("/plans/{id:guid}", async (
            Guid id,
            ApiSecurityGuard guard,
            ICommercialPlanRepository planRepo,
            ISubscriptionRepository subRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var actorGuard = MasterActorGuard.EnsureMasterPermission(user!, PermissionAction.Delete);
            if (actorGuard.IsFailure)
                return ApiSecurityGuard.ProblemFromError(actorGuard.Error);

            var plan = await planRepo.FindByIdAsync(id, ct);
            if (plan is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Plan.NotFound", "Plano não encontrado."));

            var subs = await subRepo.ListAllAsync(ct);
            if (subs.Any(s => s.PlanId == id))
            {
                return ApiSecurityGuard.ProblemFromError(Error.Conflict(
                    "Plan.InUse",
                    "Existem clientes/assinaturas vinculados a este plano. Altere o plano dos clientes antes de excluir."));
            }

            planRepo.Remove(plan);
            await planRepo.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        });

        master.MapPost("/customers-subscriptions", async (
            CreateCustomerSubReq req,
            ApiSecurityGuard guard,
            MasterPlanAndSubscriptionService planSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var res = await planSvc.CreateCustomerSubscriptionAsync(
                user!,
                req.CustomerName,
                req.DocumentNumber,
                req.BillingEmail,
                req.Phone,
                req.PlanId,
                req.StartWithTrial,
                ct);

            return res.IsFailure
                ? ApiSecurityGuard.ProblemFromError(res.Error)
                : Results.Created($"/api/v1/master/customers/{res.Value.Customer.Id}", new
                {
                    customerId = res.Value.Customer.Id,
                    subscriptionId = res.Value.Subscription.Id,
                    status = res.Value.Subscription.Status
                });
        });

        master.MapGet("/customers", async (
            ApiSecurityGuard guard,
            IMasterCustomerRepository customerRepo,
            ISubscriptionRepository subRepo,
            ICommercialPlanRepository planRepo,
            ITenantRepository tenantRepo,
            IGlobalSettingRepository settingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var customers = await customerRepo.ListAllAsync(ct);
            var subs = await subRepo.ListAllAsync(ct);
            var plans = (await planRepo.ListAllAsync(false, ct)).ToDictionary(p => p.Id, p => p);
            var tenants = await tenantRepo.ListAllAsync(ct);
            var segments = await MasterSegmentCatalogStore.GetEffectiveSegmentsAsync(settingRepo, ct);

            return Results.Ok(new
            {
                customers = customers.Select(c =>
                {
                    var latestSub = subs.Where(s => s.CustomerAccountId == c.Id).OrderByDescending(s => s.StartedAtUtc).FirstOrDefault();
                    var plan = latestSub is not null && plans.TryGetValue(latestSub.PlanId, out var pl) ? pl : null;
                    return new
                    {
                        c.Id,
                        customerName = c.Name,
                        document = c.DocumentNumber,
                        c.BillingEmail,
                        c.Phone,
                        status = c.Status.ToString(),
                        planId = plan?.Id,
                        planName = plan?.Name ?? "—",
                        subscriptionStatus = latestSub?.Status.ToString() ?? "None"
                    };
                }),
                establishments = tenants.Select(t => new
                {
                    t.Id,
                    t.CustomerAccountId,
                    t.TradeName,
                    t.LegalName,
                    t.Slug,
                    t.SegmentCode,
                    cnpj = t.Cnpj.Formatted,
                    cnpjDigits = t.Cnpj.Digits,
                    status = t.Status.ToString()
                }),
                segments
            });
        });

        master.MapPut("/customers/{id:guid}", async (
            Guid id,
            UpdateMasterCustomerReq req,
            ApiSecurityGuard guard,
            IMasterCustomerRepository customerRepo,
            ISubscriptionRepository subRepo,
            ICommercialPlanRepository planRepo,
            ITenantRepository tenantRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var actorGuard = MasterActorGuard.EnsureMasterPermission(user!, PermissionAction.Edit);
            if (actorGuard.IsFailure)
                return ApiSecurityGuard.ProblemFromError(actorGuard.Error);

            var customer = await customerRepo.FindByIdAsync(id, ct);
            if (customer is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Customer.NotFound", "Cliente não encontrado."));

            var updRes = customer.UpdateDetails(req.CustomerName, req.DocumentNumber, req.BillingEmail, req.Phone, req.Status);
            if (updRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(updRes.Error);

            if (req.PlanId.HasValue && req.PlanId.Value != Guid.Empty)
            {
                var sub = await subRepo.FindLatestByCustomerIdAsync(customer.Id, ct);
                var plan = await planRepo.FindByIdAsync(req.PlanId.Value, ct);
                if (plan is null)
                    return ApiSecurityGuard.ProblemFromError(Error.NotFound("Plan.NotFound", "Plano informado não encontrado."));

                if (sub is null)
                {
                    await subRepo.AddAsync(Subscription.Create(customer.Id, plan, clock.UtcNow, startWithTrial: false), ct);
                }
                else if (sub.PlanId != plan.Id)
                {
                    var linkedCount = (await tenantRepo.ListAllAsync(ct)).Count(t => t.CustomerAccountId == customer.Id);
                    var chRes = sub.ChangePlan(plan, new CurrentUsageSnapshot(linkedCount, 0, 0, 0, 0, 0), clock.UtcNow);
                    if (chRes.IsFailure)
                        return ApiSecurityGuard.ProblemFromError(chRes.Error);
                }
            }

            await customerRepo.SaveChangesAsync(ct);
            await subRepo.SaveChangesAsync(ct);
            return Results.Ok(new { customer.Id, customerName = customer.Name });
        });

        master.MapDelete("/customers/{id:guid}", async (
            Guid id,
            ApiSecurityGuard guard,
            IMasterCustomerRepository customerRepo,
            ISubscriptionRepository subRepo,
            ITenantRepository tenantRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var actorGuard = MasterActorGuard.EnsureMasterPermission(user!, PermissionAction.Delete);
            if (actorGuard.IsFailure)
                return ApiSecurityGuard.ProblemFromError(actorGuard.Error);

            var customer = await customerRepo.FindByIdAsync(id, ct);
            if (customer is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Customer.NotFound", "Cliente não encontrado."));

            var linkedStores = (await tenantRepo.ListAllAsync(ct)).Where(t => t.CustomerAccountId == id).ToList();
            if (linkedStores.Count > 0)
            {
                return ApiSecurityGuard.ProblemFromError(Error.Conflict(
                    "Customer.HasStores",
                    $"Existem {linkedStores.Count} loja(s) vinculadas a este cliente. Exclua as lojas primeiro antes de excluir o cliente."));
            }

            var subs = (await subRepo.ListAllAsync(ct)).Where(s => s.CustomerAccountId == id).ToList();
            foreach (var sub in subs)
                subRepo.Remove(sub);

            customerRepo.Remove(customer);
            await subRepo.SaveChangesAsync(ct);
            await customerRepo.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        });

        master.MapPut("/establishments/{id:guid}", async (
            Guid id,
            UpdateEstablishmentMasterReq req,
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var actorGuard = MasterActorGuard.EnsureMasterPermission(user!, PermissionAction.Edit, SystemModules.Tenants);
            if (actorGuard.IsFailure)
                return ApiSecurityGuard.ProblemFromError(actorGuard.Error);

            var tenant = await tenantRepo.FindByIdAsync(id, ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Tenant.NotFound", "Loja não encontrada."));

            var existingSlugOwner = await tenantRepo.FindBySlugAsync(req.Slug, ct);
            if (existingSlugOwner is not null && existingSlugOwner.Id != tenant.Id)
                return ApiSecurityGuard.ProblemFromError(Error.Conflict("Tenant.SlugAlreadyExists", $"O slug '{req.Slug}' já está em uso por outra loja."));

            var res = tenant.UpdateMasterConfiguration(req.LegalName, req.TradeName, req.Slug, req.Cnpj, req.SegmentCode, req.Status);
            if (res.IsFailure)
                return ApiSecurityGuard.ProblemFromError(res.Error);

            await tenantRepo.SaveChangesAsync(ct);
            return Results.Ok(new { tenant.Id, tenant.TradeName, tenant.Slug, tenant.SegmentCode, status = tenant.Status.ToString() });
        });

        master.MapDelete("/establishments/{id:guid}", async (
            Guid id,
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            var actorGuard = MasterActorGuard.EnsureMasterPermission(user!, PermissionAction.Delete, SystemModules.Tenants);
            if (actorGuard.IsFailure)
                return ApiSecurityGuard.ProblemFromError(actorGuard.Error);

            var tenant = await tenantRepo.FindByIdAsync(id, ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Tenant.NotFound", "Loja não encontrada."));

            setter.Clear();
            setter.SetTenant(tenant.Id, TenantResolutionSource.BackgroundJob);
            try
            {
                await tenantRepo.RemoveWithChildrenAsync(tenant, ct);
            }
            finally
            {
                setter.Clear();
                setter.SetMasterScope();
            }

            return Results.Ok(new { success = true });
        });

        master.MapGet("/segments", async (
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return Results.Ok(await MasterSegmentCatalogStore.GetEffectiveSegmentsAsync(settingRepo, ct));
        });

        master.MapPost("/segments", async (
            UpsertSegmentReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            MasterOperationsAndDashboardService ops,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return await MasterSegmentCatalogStore.UpsertSegmentAsync(user!, req, settingRepo, ops, ct);
        });

        master.MapPut("/segments/{code}", async (
            string code,
            UpsertSegmentReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            MasterOperationsAndDashboardService ops,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return await MasterSegmentCatalogStore.UpsertSegmentAsync(user!, req with { Code = code }, settingRepo, ops, ct);
        });

        master.MapDelete("/segments/{code}", async (
            string code,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            MasterOperationsAndDashboardService ops,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return await MasterSegmentCatalogStore.DeleteSegmentAsync(user!, code, settingRepo, ops, ct);
        });

        master.MapPost("/establishments/{tenantId:guid}/admin-user", async (
            Guid tenantId,
            CreateStoreUserReq req,
            ApiSecurityGuard guard,
            UserAuthorizationService userAuth,
            ITenantContextSetter setter,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            setter.Clear();
            setter.SetTenant(tenantId, TenantResolutionSource.BackgroundJob);
            try
            {
                var res = await userAuth.CreateEstablishmentUserAsync(
                    user!,
                    tenantId,
                    req.FullName,
                    req.Email,
                    req.Password,
                    EstablishmentRole.Administrator,
                    http.Connection.RemoteIpAddress?.ToString(),
                    ct);
                return ToHttp(res, StatusCodes.Status201Created);
            }
            finally
            {
                setter.Clear();
                setter.SetMasterScope();
            }
        });
    }

    private static void MapStoreAndPublicEndpoints(RouteGroupBuilder v1)
    {
        var store = v1.MapGroup("/store");

        store.MapPost("/establishments", async (
            CreateEstablishmentCommand req,
            ApiSecurityGuard guard,
            TenantProvisioningService provSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            return ToHttp(await provSvc.CreateEstablishmentAsync(user!, req, ct), StatusCodes.Status201Created);
        });

        store.MapGet("/branding", async (
            ApiSecurityGuard guard,
            StoreBrandingService brandingSvc,
            ITenantRepository tenantRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null)
                return err;

            var tenant = await tenantRepo.FindByIdAsync(user!.TenantId!.Value, ct);
            var preview = await brandingSvc.GetDraftPreviewAsync(user, user.TenantId.Value, ct);
            if (preview.IsFailure)
                return ApiSecurityGuard.ProblemFromError(preview.Error);

            return Results.Ok(new
            {
                tenantId = tenant?.Id,
                tenant?.TradeName,
                tenant?.Slug,
                cnpj = tenant?.Cnpj.Formatted,
                draft = preview.Value
            });
        });

        store.MapPut("/branding/draft", async (
            UpdateStoreBrandingDraftCommand req,
            ApiSecurityGuard guard,
            StoreBrandingService brandingSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null)
                return err;

            return ToHttp(await brandingSvc.UpdateDraftAsync(user!, user!.TenantId!.Value, req, ct));
        });

        store.MapPost("/branding/publish", async (
            ApiSecurityGuard guard,
            StoreBrandingService brandingSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null)
                return err;

            return ToHttp(await brandingSvc.PublishBrandingAsync(user!, user!.TenantId!.Value, ct));
        });

        store.MapGet("/segments", async (IGlobalSettingRepository settingRepo, CancellationToken ct) =>
            Results.Ok(await MasterSegmentCatalogStore.GetEffectiveSegmentsAsync(settingRepo, ct)));

        store.MapGet("/users", async (
            ApiSecurityGuard guard,
            IUserRepository userRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Security, ct);
            if (err is not null)
                return err;

            var list = await userRepo.ListByTenantAsync(user!.TenantId!.Value, ct);
            return Results.Ok(list.Select(u => new
            {
                u.Id,
                u.FullName,
                u.NormalizedEmail,
                role = u.EstablishmentRole?.ToString() ?? "Unknown",
                u.IsActive
            }));
        });

        store.MapPost("/users", async (
            CreateStoreUserReq req,
            ApiSecurityGuard guard,
            UserAuthorizationService userAuth,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Security, ct);
            if (err is not null)
                return err;

            var res = await userAuth.CreateEstablishmentUserAsync(
                user!,
                user!.TenantId!.Value,
                req.FullName,
                req.Email,
                req.Password,
                req.Role,
                http.Connection.RemoteIpAddress?.ToString(),
                ct);
            return ToHttp(res, StatusCodes.Status201Created);
        });

        var pub = v1.MapGroup("/public/store/{slug}");

        pub.MapGet("/", async (
            string slug,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            StoreBrandingService brandingSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            return ToHttp(await brandingSvc.GetPublicStorefrontBySlugAsync(slug, ct));
        });

        pub.MapGet("/menu", async (
            string slug,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            AcaiBuilderManagementService acaiSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var categories = await categoryRepo.ListOrderedAsync(ct);
            var products = await productRepo.ListAllOrderedAsync(ct);
            var acai = await acaiSvc.GetMenuCatalogAsync(includePaused: false, ct);

            return Results.Ok(new
            {
                tenantId = tenant.Id,
                storeTradeName = tenant.TradeName,
                usesPitakoBranding = false,
                categories = categories.Select(c => new { c.Id, c.Name, c.DisplayOrder, status = c.Status.ToString() }),
                products = products.Where(p => p.Status == CatalogItemStatus.Active).Select(p => new
                {
                    p.Id,
                    p.CategoryId,
                    p.Name,
                    p.Description,
                    price = p.BasePrice.Amount,
                    saleMode = p.SaleMode.ToString(),
                    p.IsFeatured
                }),
                acai = new
                {
                    cupSizes = acai.CupSizes.Select(c => new { c.Id, c.Name, c.VolumeMl, basePrice = c.BasePrice.Amount, c.FreeToppingsLimit }),
                    baseFlavors = acai.BaseFlavors.Select(f => new { f.Id, f.Name, extraPrice = f.ExtraPrice.Amount }),
                    freeToppings = acai.FreeToppings.Select(t => new { t.Id, t.Name, t.MaxPortionsPerCup }),
                    paidAddons = acai.PaidAddons.Select(a => new { a.Id, a.Name, price = a.AdditionalPrice.Amount, a.MaxQuantityPerCup })
                }
            });
        });

        pub.MapPost("/checkout", async (
            string slug,
            PublicCheckoutReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            IOrderRepository orderRepo,
            IOrderRealtimeNotifier realtimeNotifier,
            IClock clock,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var sessionKey = string.IsNullOrWhiteSpace(req.CartSessionKey) ? $"web-{Guid.NewGuid():N}" : req.CartSessionKey.Trim();

            var cartRes = await cartSvc.GetOrCreateCartAsync(sessionKey, req.CustomerPhone ?? req.CustomerName, ct);
            if (cartRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(cartRes.Error);

            foreach (var item in req.Items ?? [])
            {
                var addRes = await cartSvc.AddCatalogProductAsync(sessionKey, item.ProductId, null, null, item.Quantity, item.Notes, false, ct);
                if (addRes.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(addRes.Error);
            }

            if (req.AcaiCups is not null)
            {
                foreach (var cup in req.AcaiCups)
                {
                    var addCupRes = await cartSvc.AddCustomAcaiCupAsync(sessionKey, cup, false, ct);
                    if (addCupRes.IsFailure)
                        return ApiSecurityGuard.ProblemFromError(addCupRes.Error);
                }
            }

            var updatedCart = (await cartSvc.GetOrCreateCartAsync(sessionKey, null, ct)).Value;
            var seq = await orderRepo.CountAllAsync(ct) + 1;
            var orderRes = Order.CreateFromCart(
                updatedCart,
                $"WEB-{seq:D5}",
                OrderChannel.OwnDeliveryMenu,
                req.FulfillmentMode,
                req.CustomerName,
                req.CustomerPhone,
                req.DeliveryAddress,
                Money.Zero(),
                Money.Zero(),
                updatedCart.ItemsDiscount,
                updatedCart.AppliedCouponCode,
                null,
                null,
                req.IdempotencyKey,
                clock.UtcNow);

            if (orderRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(orderRes.Error);

            await orderRepo.AddAsync(orderRes.Value, ct);
            await orderRepo.SaveChangesAsync(ct);
            await realtimeNotifier.NotifyAsync(new OrderRealtimeEvent(tenant.Id, orderRes.Value.Id, orderRes.Value.OrderNumber, "OrderPlaced", orderRes.Value.Status, clock.UtcNow), ct);

            return Results.Created($"/api/v1/orders/{orderRes.Value.Id}", new
            {
                orderRes.Value.Id,
                orderRes.Value.OrderNumber,
                totalAmount = orderRes.Value.TotalAmount.Amount,
                status = orderRes.Value.Status.ToString()
            });
        });
    }

    private static void MapCatalogAndAcaiEndpoints(RouteGroupBuilder v1)
    {
        var catalog = v1.MapGroup("/catalog");

        catalog.MapGet("/products", async (
            ApiSecurityGuard guard,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            var categories = await categoryRepo.ListOrderedAsync(ct);
            var products = await productRepo.ListAllOrderedAsync(ct);
            return Results.Ok(new
            {
                categories = categories.Select(c => new { c.Id, c.Name, c.DisplayOrder, status = c.Status.ToString() }),
                products = products.Select(p => new
                {
                    p.Id,
                    p.CategoryId,
                    p.Name,
                    p.Sku,
                    basePrice = p.BasePrice.Amount,
                    status = p.Status.ToString(),
                    saleMode = p.SaleMode.ToString()
                })
            });
        });

        catalog.MapPost("/categories", async (
            CreateCategoryReq req,
            ApiSecurityGuard guard,
            CatalogManagementService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateCategoryAsync(user!, req.Name, req.Description, req.ImageUrl, req.DisplayOrder, ct), StatusCodes.Status201Created);
        });

        catalog.MapPost("/products", async (
            CreateProductReq req,
            ApiSecurityGuard guard,
            CatalogManagementService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateProductAsync(user!, req.CategoryId, req.Name, req.BasePrice, req.Description, req.Sku, req.SaleMode, req.IsFeatured, req.DisplayOrder, ct), StatusCodes.Status201Created);
        });

        catalog.MapPost("/products/{id:guid}/pause", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.PauseProductAsync(user!, id, ct));
        });

        catalog.MapPost("/products/{id:guid}/reactivate", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.ReactivateProductAsync(user!, id, ct));
        });

        var acai = v1.MapGroup("/acai");

        acai.MapGet("/config", async (ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return Results.Ok(await svc.GetMenuCatalogAsync(includePaused: true, ct));
        });

        acai.MapPost("/cup-sizes", async (CreateAcaiCupReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateCupSizeAsync(user!, req.Name, req.VolumeMl, req.BasePrice, req.FreeToppingsLimit, req.MinFreeToppings, cancellationToken: ct), StatusCodes.Status201Created);
        });

        acai.MapPost("/flavors", async (CreateAcaiFlavorReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateBaseFlavorAsync(user!, req.Name, req.ExtraPrice, cancellationToken: ct), StatusCodes.Status201Created);
        });

        acai.MapPost("/free-toppings", async (CreateAcaiFreeReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateFreeToppingAsync(user!, req.Name, req.MaxPortionsPerCup, cancellationToken: ct), StatusCodes.Status201Created);
        });

        acai.MapPost("/paid-addons", async (CreateAcaiPaidReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreatePaidAddonAsync(user!, req.Name, req.AdditionalPrice, req.MaxQuantityPerCup, cancellationToken: ct), StatusCodes.Status201Created);
        });
    }

    private static void MapInventoryAndWeighingEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/inventory/stock", async (ApiSecurityGuard guard, IStockBalanceRepository stockRepo, ISupplyItemRepository supplyRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null)
                return err;

            var stocks = await stockRepo.ListAllWithLotsAsync(ct);
            var lookup = await supplyRepo.FindLookupByIdsAsync(stocks.Select(s => s.SupplyItemId), ct);
            return Results.Ok(stocks.Select(s => new
            {
                s.SupplyItemId,
                supplyName = lookup.TryGetValue(s.SupplyItemId, out var sup) ? sup.Name : s.SupplyItemId.ToString(),
                s.QuantityOnHand
            }));
        });

        v1.MapPost("/weighing/devices", async (RegisterScaleReq req, ApiSecurityGuard guard, WeighingService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Weighing, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.RegisterScaleDeviceAsync(user!, req.Name, req.Manufacturer, req.ConnectionType, req.EndpointOrPort, "TOLEDO_PRT", 9600, req.AllowManualFallback, true, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/weighing/manual", async (ManualWeighingReq req, ApiSecurityGuard guard, WeighingService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Weighing, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.RegisterManualWeighingAsync(user!, req.ProductName, req.PricePerKg, req.GrossGrams, req.Reason, req.ScaleDeviceId, req.ProductId, req.TarePresetId, req.ManualTareGrams, ct), StatusCodes.Status201Created);
        });
    }

    private static void MapOrdersAndPdvEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/orders/kanban", async (ApiSecurityGuard guard, OrderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.GetKanbanBoardAsync(user!, ct));
        });

        v1.MapPost("/orders/{id:guid}/status", async (Guid id, TransitionOrderReq req, ApiSecurityGuard guard, OrderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.TransitionKanbanStatusAsync(user!, id, req.NextStatus, req.Reason ?? "Atualização operacional", ct));
        });

        v1.MapPost("/pdv/sessions/open", async (OpenPdvReq req, ApiSecurityGuard guard, PdvCashierService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.OpenSessionAsync(user!, req.TerminalCode, req.OpeningBalance, req.StoreUnitId, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/pdv/sales", async (SettlePdvSaleRequest req, ApiSecurityGuard guard, PdvCashierService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.SettleSaleAsync(user!, req, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/pdv/sessions/{id:guid}/close", async (Guid id, ClosePdvReq req, ApiSecurityGuard guard, PdvCashierService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CloseSessionAsync(user!, id, req.CountedCashAmount, req.Notes, ct));
        });
    }

    private static void MapDiningAndSelfServiceEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/dining/tables", async (ApiSecurityGuard guard, IDiningSectorRepository sectorRepo, IDiningTableRepository tableRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null)
                return err;

            var sectors = await sectorRepo.ListOrderedAsync(ct);
            var tables = new List<DiningTable>();
            foreach (var sector in sectors)
            {
                tables.AddRange(await tableRepo.ListBySectorAsync(sector.Id, ct));
            }
            return Results.Ok(tables.Select(t => new { t.Id, t.SectorId, t.TableCode, t.Capacity, t.QrCodeToken, status = t.Status.ToString(), t.CurrentTabId }));
        });

        v1.MapPost("/dining/sectors", async (CreateSectorReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateSectorAsync(user!, req.Name, req.StoreUnitId, req.DisplayOrder, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/dining/tables", async (CreateTableReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateTableAsync(user!, req.SectorId, req.TableCode, req.Capacity, req.QrCodeToken, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/public/qr/{slug}/{token}/session", async (
            string slug,
            string token,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            QrTableSelfServiceService qrSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            return ToHttp(await qrSvc.StartOrResumeByQrTokenAsync(token, cancellationToken: ct));
        });

        v1.MapPost("/public/qr/{slug}/{token}/waiter-call", async (
            string slug,
            string token,
            QrWaiterCallReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            QrTableSelfServiceService qrSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            return ToHttp(await qrSvc.CallWaiterAsync(token, req.CallType, req.Notes, ct), StatusCodes.Status201Created);
        });
    }

    private static void MapPrintingMarketplaceWhatsAppEndpoints(RouteGroupBuilder v1)
    {
        v1.MapPost("/printing/printers", async (RegisterPrinterReq req, ApiSecurityGuard guard, PrintingManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Printing, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.RegisterPrinterAsync(user!, req.Code, req.DisplayName, req.ConnectionKind, req.EndpointOrPort, req.PaperWidthMm, cancellationToken: ct), StatusCodes.Status201Created);
        });

        v1.MapGet("/marketplaces/health", async (ApiSecurityGuard guard, MarketplaceBulkSyncService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.GetMultiStoreHealthDashboardAsync(user!, ct));
        });

        v1.MapGet("/whatsapp/inbox", async (ApiSecurityGuard guard, WhatsAppSupportService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.WhatsApp, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.ListInboxAsync(user!, ct));
        });
    }

    private static void MapFinanceReportsLifecycleOfflineEndpoints(RouteGroupBuilder v1)
    {
        var finance = v1.MapGroup("/finance");

        finance.MapPost("/entries", async (CreateFinanceEntryReq req, ApiSecurityGuard guard, FinanceManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateEntryAsync(user!, req.Kind, req.DreCategory, req.CounterpartyName, req.Description, req.Amount, req.DueDateUtc, req.ReferenceDocument, req.StoreUnitId, ct), StatusCodes.Status201Created);
        });

        finance.MapPost("/entries/{id:guid}/settle", async (Guid id, SettleFinanceEntryReq req, ApiSecurityGuard guard, FinanceManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.SettleEntryAsync(user!, id, req.NetPaidAmount, req.FeeAmount, req.PaymentMethod, ct));
        });

        finance.MapGet("/cash-flow", async (DateTimeOffset? startUtc, DateTimeOffset? endUtc, ApiSecurityGuard guard, FinanceManagementService svc, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            var s = startUtc ?? clock.UtcNow.AddDays(-30);
            var e = endUtc ?? clock.UtcNow.AddDays(30);
            return ToHttp(await svc.GetCashFlowSummaryAsync(user!, s, e, ct));
        });

        finance.MapGet("/dre", async (DateTimeOffset? startUtc, DateTimeOffset? endUtc, ApiSecurityGuard guard, FinanceManagementService svc, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            var s = startUtc ?? clock.UtcNow.AddDays(-30);
            var e = endUtc ?? clock.UtcNow.AddDays(30);
            return ToHttp(await svc.GetDreStatementAsync(user!, s, e, ct));
        });

        finance.MapPost("/gateways", async (ConfigureGatewayReq req, ApiSecurityGuard guard, FinanceManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.ConfigurePaymentGatewayAsync(user!, req.Provider, req.MerchantAccountCode, req.ApiSecret, req.MdrPercent, req.SettlementDays, ct), StatusCodes.Status201Created);
        });

        finance.MapPost("/gateways/{gatewayId:guid}/charges", async (Guid gatewayId, ExternalGatewayChargeRequest req, ApiSecurityGuard guard, FinanceManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.ProcessGatewayChargeAsync(user!, gatewayId, req, ct));
        });

        v1.MapPost("/reports/export", async (ExportReportReq req, ApiSecurityGuard guard, ReportingManagementService svc, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Reports, ct);
            if (err is not null)
                return err;

            var filter = new ReportQueryFilter(req.Category, req.PeriodStartUtc ?? clock.UtcNow.AddDays(-30), req.PeriodEndUtc ?? clock.UtcNow.AddDays(1));
            var res = await svc.ExportReportAsync(user!, filter, req.Format, ct);
            if (res.IsFailure)
                return ApiSecurityGuard.ProblemFromError(res.Error);

            return Results.File(res.Value.ContentBytes, res.Value.MimeType, res.Value.FileName);
        });

        v1.MapPost("/lifecycle/backups", async (ApiSecurityGuard guard, DataLifecycleService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreateEncryptedBackupAsync(user!, cancellationToken: ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/offline/terminals", async (RegisterOfflineTerminalReq req, ApiSecurityGuard guard, OfflineSynchronizationService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.RegisterTerminalAsync(user!, req.TerminalCode, req.DeviceFingerprint, req.CatalogSnapshotVersion, req.MaxOfflineHours, req.StoreUnitId, ct), StatusCodes.Status201Created);
        });

        v1.MapPost("/offline/sync", async (OfflineSyncReq req, ApiSecurityGuard guard, OfflineSynchronizationService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.SynchronizeBatchAsync(user!, req.TerminalId, req.Operations, ct));
        });
    }

    private static IResult ToHttp<T>(Result<T> result, int successCode = StatusCodes.Status200OK) =>
        result.IsFailure
            ? ApiSecurityGuard.ProblemFromError(result.Error)
            : Results.Json(result.Value, statusCode: successCode);

    private static IResult ToHttp(Result result) =>
        result.IsFailure
            ? ApiSecurityGuard.ProblemFromError(result.Error)
            : Results.Ok(new { success = true });
}

public sealed record SaasLoginReq(string Email, string Password);
public sealed record StoreLoginReq(string StoreSlug, string Email, string Password);
public sealed record PasswordRecoveryReq(string Email, string? StoreSlug = null);
public sealed record CreateMasterPlanReq(string Code, string Name, decimal PriceAmount, string? Description = null, int TrialDays = 14, bool IsUnlimited = false, int? MaxEstablishments = null, int? MaxUnits = null, int? MaxMarketplaces = null, int? MaxUsers = null, int? MaxDevices = null, int? MaxSessions = null, int? MaxMonthlyOrders = null, IReadOnlyList<string>? EnabledModules = null, bool? IsActive = null);
public sealed record CreateCustomerSubReq(string CustomerName, string DocumentNumber, string BillingEmail, string? Phone, Guid PlanId, bool StartWithTrial = false);
public sealed record UpdateMasterCustomerReq(string CustomerName, string DocumentNumber, string BillingEmail, string? Phone, CustomerAccountStatus Status, Guid? PlanId = null);
public sealed record UpdateEstablishmentMasterReq(string LegalName, string TradeName, string Slug, string Cnpj, string SegmentCode, TenantStatus Status);
public sealed record UpsertSegmentReq(string Code, string DisplayName, bool SupportsAcaiBuilder, bool SupportsWeightScale, bool SupportsTableService, bool SupportsFractionalItems);

internal static class MasterSegmentCatalogStore
{
    private const string CustomKey = "master.custom_segments";
    private const string HiddenKey = "master.hidden_segments";

    public static async Task<IReadOnlyList<BusinessSegmentProfile>> GetEffectiveSegmentsAsync(
        IGlobalSettingRepository settingRepo,
        CancellationToken ct)
    {
        var customSetting = await settingRepo.FindByKeyAsync(CustomKey, ct);
        var hiddenSetting = await settingRepo.FindByKeyAsync(HiddenKey, ct);

        var hidden = string.IsNullOrWhiteSpace(hiddenSetting?.Value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (JsonSerializer.Deserialize<HashSet<string>>(hiddenSetting.Value) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var customList = string.IsNullOrWhiteSpace(customSetting?.Value)
            ? []
            : (JsonSerializer.Deserialize<List<BusinessSegmentProfile>>(customSetting.Value) ?? []);

        var map = new Dictionary<string, BusinessSegmentProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in BusinessSegmentCatalog.AllBuiltIn)
        {
            if (!hidden.Contains(item.Code))
                map[item.Code] = item;
        }
        foreach (var item in customList)
        {
            if (!hidden.Contains(item.Code))
                map[item.Code] = item;
        }
        return map.Values.OrderBy(v => v.DisplayName).ToList();
    }

    public static async Task<IResult> UpsertSegmentAsync(
        User actor,
        UpsertSegmentReq req,
        IGlobalSettingRepository settingRepo,
        MasterOperationsAndDashboardService ops,
        CancellationToken ct)
    {
        var check = BusinessSegmentCatalog.Resolve(req.Code);
        if (check.IsFailure)
            return ApiSecurityGuard.ProblemFromError(check.Error);
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return ApiSecurityGuard.ProblemFromError(Error.Validation("Segment.DisplayNameRequired", "Nome da modalidade é obrigatório."));

        var normalizedCode = BusinessSegmentCatalog.Normalize(req.Code);
        var customSetting = await settingRepo.FindByKeyAsync(CustomKey, ct);
        var customList = string.IsNullOrWhiteSpace(customSetting?.Value)
            ? []
            : (JsonSerializer.Deserialize<List<BusinessSegmentProfile>>(customSetting.Value) ?? []);

        customList.RemoveAll(s => s.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
        var saved = new BusinessSegmentProfile(
            normalizedCode,
            req.DisplayName.Trim(),
            req.SupportsAcaiBuilder,
            req.SupportsWeightScale,
            req.SupportsTableService,
            req.SupportsFractionalItems);
        customList.Add(saved);

        var hiddenSetting = await settingRepo.FindByKeyAsync(HiddenKey, ct);
        if (hiddenSetting is not null && !string.IsNullOrWhiteSpace(hiddenSetting.Value))
        {
            var hidden = JsonSerializer.Deserialize<HashSet<string>>(hiddenSetting.Value) ?? [];
            if (hidden.Remove(normalizedCode))
                await ops.UpsertGlobalSettingAsync(actor, HiddenKey, JsonSerializer.Serialize(hidden), "Modalidades ocultas", false, ct);
        }

        await ops.UpsertGlobalSettingAsync(actor, CustomKey, JsonSerializer.Serialize(customList), "Modalidades customizadas", false, ct);
        return Results.Ok(saved);
    }

    public static async Task<IResult> DeleteSegmentAsync(
        User actor,
        string code,
        IGlobalSettingRepository settingRepo,
        MasterOperationsAndDashboardService ops,
        CancellationToken ct)
    {
        var normalized = BusinessSegmentCatalog.Normalize(code);
        var customSetting = await settingRepo.FindByKeyAsync(CustomKey, ct);
        var customList = string.IsNullOrWhiteSpace(customSetting?.Value)
            ? []
            : (JsonSerializer.Deserialize<List<BusinessSegmentProfile>>(customSetting.Value) ?? []);
        customList.RemoveAll(s => s.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        await ops.UpsertGlobalSettingAsync(actor, CustomKey, JsonSerializer.Serialize(customList), "Modalidades customizadas", false, ct);

        if (BusinessSegmentCatalog.AllBuiltIn.Any(s => s.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            var hiddenSetting = await settingRepo.FindByKeyAsync(HiddenKey, ct);
            var hidden = string.IsNullOrWhiteSpace(hiddenSetting?.Value)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (JsonSerializer.Deserialize<HashSet<string>>(hiddenSetting.Value) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            hidden.Add(normalized);
            await ops.UpsertGlobalSettingAsync(actor, HiddenKey, JsonSerializer.Serialize(hidden), "Modalidades ocultas", false, ct);
        }

        return Results.Ok(new { success = true });
    }
}
public sealed record CreateStoreUserReq(string FullName, string Email, string Password, EstablishmentRole Role);
public sealed record PublicCheckoutItemReq(Guid ProductId, int Quantity = 1, string? Notes = null);
public sealed record PublicCheckoutReq(string CustomerName, string? CustomerPhone, FulfillmentMode FulfillmentMode, string? DeliveryAddress = null, string? CartSessionKey = null, string? IdempotencyKey = null, IReadOnlyList<PublicCheckoutItemReq>? Items = null, IReadOnlyList<AcaiCupBuildRequest>? AcaiCups = null);
public sealed record CreateCategoryReq(string Name, string? Description = null, string? ImageUrl = null, int DisplayOrder = 0);
public sealed record CreateProductReq(Guid CategoryId, string Name, decimal BasePrice, string? Description = null, string? Sku = null, ProductSaleMode SaleMode = ProductSaleMode.Unit, bool IsFeatured = false, int DisplayOrder = 0);
public sealed record CreateAcaiCupReq(string Name, int VolumeMl, decimal BasePrice, int FreeToppingsLimit, int MinFreeToppings = 0);
public sealed record CreateAcaiFlavorReq(string Name, decimal ExtraPrice = 0m);
public sealed record CreateAcaiFreeReq(string Name, int MaxPortionsPerCup = 1);
public sealed record CreateAcaiPaidReq(string Name, decimal AdditionalPrice, int MaxQuantityPerCup = 3);
public sealed record RegisterScaleReq(string Name, ScaleManufacturer Manufacturer, ScaleConnectionType ConnectionType, string EndpointOrPort, bool AllowManualFallback = true);
public sealed record ManualWeighingReq(string ProductName, decimal PricePerKg, decimal GrossGrams, string Reason, Guid? ScaleDeviceId = null, Guid? ProductId = null, Guid? TarePresetId = null, decimal? ManualTareGrams = null);
public sealed record TransitionOrderReq(OrderStatus NextStatus, string? Reason = null);
public sealed record OpenPdvReq(string TerminalCode, decimal OpeningBalance, Guid? StoreUnitId = null);
public sealed record ClosePdvReq(decimal CountedCashAmount, string? Notes = null);
public sealed record CreateSectorReq(string Name, Guid? StoreUnitId = null, int DisplayOrder = 0);
public sealed record CreateTableReq(Guid SectorId, string TableCode, int Capacity, string? QrCodeToken = null);
public sealed record QrWaiterCallReq(WaiterCallType CallType, string? Notes = null);
public sealed record RegisterPrinterReq(string Code, string DisplayName, PrinterConnectionKind ConnectionKind, string EndpointOrPort, PaperWidthMm PaperWidthMm = PaperWidthMm.Mm80);
public sealed record CreateFinanceEntryReq(FinancialEntryKind Kind, DreLineCategory DreCategory, string CounterpartyName, string Description, decimal Amount, DateTimeOffset DueDateUtc, string? ReferenceDocument = null, Guid? StoreUnitId = null);
public sealed record SettleFinanceEntryReq(decimal NetPaidAmount, decimal FeeAmount, string PaymentMethod);
public sealed record ConfigureGatewayReq(PaymentGatewayProvider Provider, string MerchantAccountCode, string ApiSecret, decimal MdrPercent, int SettlementDays = 1);
public sealed record ExportReportReq(ReportCategory Category, ReportExportFormat Format, DateTimeOffset? PeriodStartUtc = null, DateTimeOffset? PeriodEndUtc = null);
public sealed record RegisterOfflineTerminalReq(string TerminalCode, string DeviceFingerprint, int CatalogSnapshotVersion = 1, int MaxOfflineHours = 48, Guid? StoreUnitId = null);
public sealed record OfflineSyncReq(Guid TerminalId, IReadOnlyList<OfflineOperationInput> Operations);
