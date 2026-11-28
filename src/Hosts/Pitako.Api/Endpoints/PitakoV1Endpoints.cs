using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pitako.Api.Bootstrap;
using Pitako.Api.Security;
using Pitako.Api.Services;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DataLifecycle.Application.Services;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
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
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Endpoints;

public static class PitakoV1Endpoints
{
    private static readonly SemaphoreSlim PlanCreateGate = new(1, 1);
    private static readonly SemaphoreSlim CustomerCreateGate = new(1, 1);
    private static readonly SemaphoreSlim StoreCreateGate = new(1, 1);

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
        PitakoUniversalMenuEndpoints.MapUniversalMenuAndAdminExtensions(v1);
        PitakoOperationsEndpoints.MapOperationalExtensions(v1);
        PitakoStoreSuiteEndpoints.MapStoreSuiteExtensions(v1);

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
            SecurityDbContext secDb,
            PitakoEmailService emailSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var smtpCfg = await emailSvc.GetSettingsAsync(ct);
            var webBase = !string.IsNullOrWhiteSpace(req.WebBaseUrl)
                ? req.WebBaseUrl.Trim().TrimEnd('/')
                : emailSvc.ResolveWebBaseUrl(smtpCfg, http.Request);
            var resetBase = $"{webBase}/reset-password";
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var cleanEmail = (req.Email ?? string.Empty).Trim();
            string? devLinkPreview = null;

            if (!string.IsNullOrWhiteSpace(req.StoreSlug))
            {
                var tenant = await tenantRepo.FindBySlugAsync(req.StoreSlug.Trim().ToLowerInvariant(), ct);
                if (tenant is not null)
                {
                    var estRes = await recoverySvc.RequestEstablishmentPasswordResetAsync(tenant.Id, cleanEmail, resetBase, ip, ct);
                    if (!string.IsNullOrWhiteSpace(estRes.GeneratedSecureLink))
                    {
                        devLinkPreview = estRes.GeneratedSecureLink;
                        await emailSvc.SendAsync(
                            cleanEmail,
                            $"Redefinição de senha — {tenant.TradeName}",
                            $"Olá!\n\nRecebemos uma solicitação para redefinir sua senha da loja {tenant.TradeName} ({tenant.Slug}).\n\nAcesse o link abaixo para criar uma nova senha:\n{estRes.GeneratedSecureLink}\n\nValidade: 30 minutos.",
                            kind: "PasswordReset",
                            ct: ct);
                        return Results.Ok(new { accepted = true, message = "Se a conta existir, enviamos o link de redefinição para o e-mail informado.", resetLinkOutbox = devLinkPreview });
                    }
                }
            }

            var saasRes = await recoverySvc.RequestSaasPasswordResetAsync(cleanEmail, resetBase, ip, ct);
            if (!string.IsNullOrWhiteSpace(saasRes.GeneratedSecureLink))
            {
                devLinkPreview = saasRes.GeneratedSecureLink;
                await emailSvc.SendAsync(
                    cleanEmail,
                    "Redefinição de senha — Acesso Master",
                    $"Olá!\n\nAcesse o link abaixo para redefinir sua senha:\n{saasRes.GeneratedSecureLink}\n\nValidade: 30 minutos.",
                    kind: "PasswordReset",
                    ct: ct);
                return Results.Ok(new { accepted = true, message = "Enviamos o link de redefinição de senha para o e-mail cadastrado.", resetLinkOutbox = devLinkPreview });
            }

            var normalized = User.NormalizeEmail(cleanEmail);
            var matchedEstUsers = await secDb.Users
                .AsNoTracking()
                .Where(u => u.Scope == UserScope.Establishment && u.TenantId != null && u.IsActive && u.NormalizedEmail == normalized)
                .ToListAsync(ct);

            foreach (var eu in matchedEstUsers)
            {
                var t = await tenantRepo.FindByIdAsync(eu.TenantId!.Value, ct);
                var estRes = await recoverySvc.RequestEstablishmentPasswordResetAsync(eu.TenantId!.Value, cleanEmail, resetBase, ip, ct);
                if (!string.IsNullOrWhiteSpace(estRes.GeneratedSecureLink))
                {
                    devLinkPreview ??= estRes.GeneratedSecureLink;
                    await emailSvc.SendAsync(
                        cleanEmail,
                        $"Redefinição de senha — {t?.TradeName ?? "Sua Loja"}",
                        $"Olá {eu.FullName}!\n\nRecebemos uma solicitação de nova senha para o painel da loja {t?.TradeName ?? ""} (slug: {t?.Slug ?? ""}).\n\nAcesse o link abaixo para definir sua nova senha:\n{estRes.GeneratedSecureLink}\n\nLogin do painel: {webBase}/login?loja={t?.Slug}\nValidade: 30 minutos.",
                        kind: "PasswordReset",
                        ct: ct);
                }
            }

            return Results.Ok(new { accepted = true, message = "Se o e-mail estiver cadastrado, enviamos as instruções de redefinição.", resetLinkOutbox = devLinkPreview });
        });

        auth.MapPost("/password-reset/complete", async (
            CompletePasswordResetReq req,
            PasswordRecoveryService recoverySvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var res = await recoverySvc.CompletePasswordResetAsync(
                req.Token,
                req.NewPassword,
                http.Connection.RemoteIpAddress?.ToString(),
                ct);
            return ToHttp(res);
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

            await PlanCreateGate.WaitAsync(ct);
            try
            {
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
            }
            finally
            {
                PlanCreateGate.Release();
            }
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
            ICommercialPlanRepository planRepo,
            PitakoEmailService emailSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            await CustomerCreateGate.WaitAsync(ct);
            try
            {
                var res = await planSvc.CreateCustomerSubscriptionAsync(
                    user!,
                    req.CustomerName,
                    req.DocumentNumber,
                    req.BillingEmail,
                    req.Phone,
                    req.PlanId,
                    req.StartWithTrial,
                    ct);

                if (res.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(res.Error);

                var plan = await planRepo.FindByIdAsync(req.PlanId, ct);
                var smtpCfg = await emailSvc.GetSettingsAsync(ct);
                var webBase = emailSvc.ResolveWebBaseUrl(smtpCfg, http.Request);
                await emailSvc.SendAsync(
                    req.BillingEmail,
                    "Boas-vindas — Seu cadastro na plataforma foi criado!",
                    $"Olá {req.CustomerName}!\n\nSeja bem-vindo(a)! Seu cadastro foi criado com sucesso no plano {plan?.Name ?? "Selecionado"}.\n\nAssim que sua loja for ativada, acesse seu painel em: {webBase}/login\nQualquer dúvida, use a opção \"Esqueci minha senha\" na tela de login informando este e-mail cadastrado ({req.BillingEmail}).",
                    kind: "WelcomeCustomer",
                    ct: ct);

                return Results.Created($"/api/v1/master/customers/{res.Value.Customer.Id}", new
                {
                    customerId = res.Value.Customer.Id,
                    subscriptionId = res.Value.Subscription.Id,
                    status = res.Value.Subscription.Status,
                    welcomeEmailSent = true
                });
            }
            finally
            {
                CustomerCreateGate.Release();
            }
        });

        master.MapGet("/email-settings", async (ApiSecurityGuard guard, PitakoEmailService emailSvc, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null) return err;
            var cfg = await emailSvc.GetSettingsAsync(ct);
            return Results.Ok(new
            {
                cfg.Host,
                cfg.Port,
                cfg.UseSsl,
                cfg.Username,
                hasPassword = !string.IsNullOrWhiteSpace(cfg.Password),
                cfg.FromEmail,
                cfg.FromName,
                cfg.PublicWebBaseUrl
            });
        });

        master.MapPut("/email-settings", async (SaveEmailSmtpReq req, ApiSecurityGuard guard, PitakoEmailService emailSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null) return err;
            var current = await emailSvc.GetSettingsAsync(ct);
            var next = new EmailSmtpSettingsDto(
                req.Host,
                req.Port,
                req.UseSsl,
                req.Username,
                string.IsNullOrWhiteSpace(req.Password) ? current.Password : req.Password,
                req.FromEmail,
                req.FromName,
                req.PublicWebBaseUrl);
            await emailSvc.SaveSettingsAsync(next, user!.Id, ct);
            return Results.Ok(new { saved = true });
        });

        master.MapGet("/email-outbox", async (ApiSecurityGuard guard, PitakoEmailService emailSvc, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null) return err;
            return Results.Ok(await emailSvc.ListOutboxAsync(ct));
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

            var allCustomers = await customerRepo.ListAllAsync(ct);
            var nameTrimmed = customer.Name.Trim();
            var docDigits = new string(customer.DocumentNumber.Where(char.IsDigit).ToArray());
            var emailLower = customer.BillingEmail.Trim().ToLowerInvariant();
            if (allCustomers.Any(c => c.Id != customer.Id &&
                (c.Name.Trim().Equals(nameTrimmed, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(docDigits) && new string(c.DocumentNumber.Where(char.IsDigit).ToArray()) == docDigits) ||
                 c.BillingEmail.Trim().Equals(emailLower, StringComparison.OrdinalIgnoreCase))))
            {
                return ApiSecurityGuard.ProblemFromError(
                    Error.Conflict("Customer.AlreadyExists", "Já existe outro cliente/dono cadastrado com este nome, CPF/CNPJ ou e-mail."));
            }

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

            var allTenants = await tenantRepo.ListAllAsync(ct);
            var trimmedTrade = tenant.TradeName.Trim();
            if (allTenants.Any(t => t.Id != tenant.Id &&
                (t.Cnpj.Digits == tenant.Cnpj.Digits ||
                 t.TradeName.Trim().Equals(trimmedTrade, StringComparison.OrdinalIgnoreCase))))
                return ApiSecurityGuard.ProblemFromError(Error.Conflict("Tenant.DuplicateStore", "Já existe outra loja cadastrada com este nome ou CNPJ."));

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

        master.MapGet("/overview-indicators", async (
            ApiSecurityGuard guard,
            MasterOperationsAndDashboardService ops,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null) return err;
            return ToHttp(await ops.GetOverviewIndicatorsAsync(user!, ct));
        });

        master.MapPost("/billing/sweep-delinquency", async (
            int? gracePeriodDays,
            ApiSecurityGuard guard,
            MasterPlanAndSubscriptionService planSvc,
            ISubscriptionRepository subRepo,
            ITenantRepository tenantRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null) return err;

            var summaryRes = await planSvc.ProcessDelinquencyAsync(user!, Math.Max(0, gracePeriodDays ?? 3), ct);
            if (summaryRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(summaryRes.Error);

            var subs = await subRepo.ListAllAsync(ct);
            var blockedCustomerIds = subs
                .Where(s => s.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
                .Select(s => s.CustomerAccountId)
                .ToHashSet();

            var tenants = await tenantRepo.ListAllAsync(ct);
            var suspendedStores = 0;
            foreach (var t in tenants)
            {
                if (t.CustomerAccountId.HasValue &&
                    blockedCustomerIds.Contains(t.CustomerAccountId.Value) &&
                    t.Status is TenantStatus.Trial or TenantStatus.Active)
                {
                    t.Suspend();
                    suspendedStores++;
                }
            }
            if (suspendedStores > 0)
                await tenantRepo.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                summaryRes.Value.NewlyOverdueInvoices,
                summaryRes.Value.SuspendedSubscriptions,
                suspendedStores
            });
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

            return await MasterSegmentCatalogStore.UpsertSegmentAsync(user!, req, settingRepo, ops, createOnly: true, ct);
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

            return await MasterSegmentCatalogStore.UpsertSegmentAsync(user!, req with { Code = code }, settingRepo, ops, createOnly: false, ct);
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
            ITenantRepository tenantRepo,
            IMasterCustomerRepository customerRepo,
            PitakoEmailService emailSvc,
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

                if (res.IsSuccess)
                {
                    var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
                    var smtpCfg = await emailSvc.GetSettingsAsync(ct);
                    var webBase = emailSvc.ResolveWebBaseUrl(smtpCfg, http.Request);
                    var slug = tenant?.Slug ?? "";
                    var storeName = tenant?.TradeName ?? "Sua Loja";
                    var adminUrl = $"{webBase}/login?loja={slug}";
                    var menuUrl = $"{webBase}/loja/{slug}";
                    var portalUrl = $"{webBase}/portal/{slug}";
                    var msgBody =
                        $"Olá {req.FullName}!\n\n" +
                        $"Seja bem-vindo(a)! Seu painel administrativo e cardápio digital da loja {storeName} já estão prontos.\n\n" +
                        $"Link de Acesso ao Painel Adm: {adminUrl}\n" +
                        $"Portal da Loja: {portalUrl}\n" +
                        $"Cardápio Digital dos Clientes: {menuUrl}\n\n" +
                        $"Dados para entrar:\n" +
                        $"- Slug da Loja: {slug}\n" +
                        $"- E-mail: {req.Email}\n" +
                        $"- Senha inicial: {req.Password}\n\n" +
                        $"Caso esqueça sua senha, clique em \"Esqueci minha senha\" na tela de login informando o e-mail {req.Email}.";

                    await emailSvc.SendAsync(
                        req.Email,
                        $"Acesso liberado ao Painel Adm — {storeName}",
                        msgBody,
                        kind: "StoreOnboarding",
                        ct: ct);

                    if (tenant?.CustomerAccountId.HasValue == true)
                    {
                        var cust = await customerRepo.FindByIdAsync(tenant.CustomerAccountId.Value, ct);
                        if (cust is not null && !string.Equals(cust.BillingEmail, req.Email, StringComparison.OrdinalIgnoreCase))
                        {
                            await emailSvc.SendAsync(
                                cust.BillingEmail,
                                $"Acesso liberado ao Painel Adm — {storeName}",
                                msgBody,
                                kind: "StoreOnboardingCopy",
                                ct: ct);
                        }
                    }
                }

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
            IServiceScopeFactory scopeFactory,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Saas, ct: ct);
            if (err is not null)
                return err;

            await StoreCreateGate.WaitAsync(ct);
            try
            {
                var res = await provSvc.CreateEstablishmentAsync(user!, req, ct);
                if (res.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(res.Error);

                using (var provScope = scopeFactory.CreateScope())
                {
                    var starter = provScope.ServiceProvider.GetRequiredService<StoreStarterProvisioner>();
                    await starter.ProvisionCompleteNewStoreAsync(res.Value.Tenant, ct);
                }

                return Results.Json(res.Value, statusCode: StatusCodes.Status201Created);
            }
            finally
            {
                StoreCreateGate.Release();
            }
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
                tenant?.LegalName,
                tenant?.TradeName,
                tenant?.Slug,
                tenant?.SegmentCode,
                tenant?.Address,
                tenant?.ContactPhone,
                tenant?.ContactEmail,
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
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se suspensa e não pode receber novos pedidos."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            return ToHttp(await brandingSvc.GetPublicStorefrontBySlugAsync(slug, ct));
        });

        pub.MapGet("/menu", async (
            string slug,
            ITenantRepository tenantRepo,
            IStoreVisualIdentityRepository identityRepo,
            ITenantContextSetter setter,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            IModifierGroupRepository modifierGroupRepo,
            IComboRepository comboRepo,
            IPromotionRepository promotionRepo,
            IOrderRepository orderRepo,
            IDeliveryPolicyRepository deliveryPolicyRepo,
            ITechnicalSheetRepository sheetRepo,
            AcaiBuilderManagementService acaiSvc,
            IClock clock,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se suspensa e não pode receber novos pedidos."));
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se temporariamente suspensa pela administração."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var identity = await identityRepo.FindByTenantIdForPublicViewAsync(tenant.Id, ct);
            var categories = await categoryRepo.ListOrderedAsync(ct);
            var products = await productRepo.ListAllOrderedAsync(ct);
            var modifierGroups = await modifierGroupRepo.ListAllAsync(ct);
            var combos = await comboRepo.ListAllOrderedAsync(ct);
            var promotions = await promotionRepo.ListActiveCandidatesAsync(ct);
            var salesCounts = await orderRepo.GetProductSalesCountsAsync(ct);
            var deliveryPolicy = await deliveryPolicyRepo.FindEffectivePolicyAsync(null, ct);
            var sheets = await sheetRepo.ListAllAsync(ct);
            var sheetByProduct = sheets
                .Where(s => s.IsActive && s.ProductId.HasValue)
                .GroupBy(s => s.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            var acai = await acaiSvc.GetMenuCatalogAsync(includePaused: false, ct);

            var utcNow = clock.UtcNow;
            var activePromos = promotions.Where(pr => pr.IsActiveAt(utcNow)).ToList();
            var opHoursJson = identity?.PublishedOperatingHoursJson ?? identity?.DraftOperatingHoursJson;
            var operatingStatus = StoreOperatingHoursHelper.Evaluate(opHoursJson, identity?.IsTemporarilyPaused ?? false, identity?.PauseReason, utcNow);

            var categoryMap = categories.ToDictionary(c => c.Id);
            var productMap = products.ToDictionary(p => p.Id);
            var activeProducts = products.Where(p => p.Status == CatalogItemStatus.Active).ToList();

            var productDtos = activeProducts.Select(p =>
            {
                var directEffective = p.GetDirectEffectiveBasePrice().Amount;
                decimal bestPrice = directEffective;
                string? appliedPromoName = directEffective < p.BasePrice.Amount ? "Oferta do item" : null;
                foreach (var promo in activePromos)
                {
                    if (!promo.AppliesTo(p)) continue;
                    var discounted = promo.CalculatePromotionalPrice(p.BasePrice).Amount;
                    if (discounted < bestPrice)
                    {
                        bestPrice = discounted;
                        appliedPromoName = promo.Name;
                    }
                }
                var primaryImg = p.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl;
                var salesCount = salesCounts.GetValueOrDefault(p.Id, 0);
                var prodSched = p.IsScheduledAvailableAt(utcNow);
                var catSched = !categoryMap.TryGetValue(p.CategoryId, out var catEntity) || catEntity.IsAvailableAt(utcNow);
                var isInStock = !p.TrackInventory || p.StockQuantity > 0;
                var discountPct = p.BasePrice.Amount > 0m && bestPrice < p.BasePrice.Amount
                    ? (int)Math.Round((p.BasePrice.Amount - bestPrice) / p.BasePrice.Amount * 100m, MidpointRounding.AwayFromZero)
                    : 0;
                var productSheet = sheetByProduct.TryGetValue(p.Id, out var matchedSheet) ? matchedSheet : null;
                var ingredients = productSheet is not null
                    ? productSheet.Ingredients
                        .Select(i => i.SupplyItemNameSnapshot)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>();
                var yieldLabel = productSheet is not null && productSheet.YieldQuantity > 0m && !string.IsNullOrWhiteSpace(productSheet.YieldUnitCode)
                    ? $"{productSheet.YieldQuantity:0.##} {productSheet.YieldUnitCode}"
                    : null;

                return new
                {
                    p.Id,
                    p.CategoryId,
                    p.Name,
                    p.Description,
                    p.Sku,
                    price = p.BasePrice.Amount,
                    promotionalPrice = p.PromotionalPrice?.Amount,
                    effectivePrice = bestPrice,
                    discountAmount = decimal.Round(p.BasePrice.Amount - bestPrice, 2),
                    discountPercent = discountPct,
                    appliedPromotionName = appliedPromoName,
                    saleMode = p.SaleMode.ToString(),
                    pricingStrategy = p.PricingStrategy.ToString(),
                    pricingStrategyCode = (int)p.PricingStrategy,
                    p.IsFeatured,
                    p.IsNew,
                    p.IsRecommended,
                    p.AllowsNotes,
                    p.PreparationMinutes,
                    ingredients,
                    yieldLabel,
                    p.TrackInventory,
                    p.StockQuantity,
                    p.AutoPauseOnZeroStock,
                    isInStock,
                    isScheduledAvailable = prodSched && catSched,
                    p.ServesPeopleCount,
                    dietaryTags = string.IsNullOrWhiteSpace(p.DietaryTagsCsv) ? Array.Empty<string>() : p.DietaryTagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    tags = string.IsNullOrWhiteSpace(p.TagsCsv) ? Array.Empty<string>() : p.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    p.CustomTemplateKey,
                    p.DisplayOrder,
                    salesCount,
                    primaryImageUrl = primaryImg,
                    images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new { i.Id, i.ImageUrl, i.DisplayOrder, i.IsPrimary }),
                    variations = p.Variations.Where(v => v.IsActive).OrderBy(v => v.DisplayOrder).Select(v => new
                    {
                        v.Id,
                        variationName = v.Name,
                        v.Description,
                        price = v.Price.Amount,
                        v.MeasureAmount,
                        v.MeasureUnit,
                        v.MaxFlavors,
                        v.Sku,
                        v.DisplayOrder,
                        v.TrackInventory,
                        v.StockQuantity,
                        isInStock = !v.TrackInventory || v.StockQuantity > 0
                    }),
                    modifierGroupIds = p.ModifierLinks.OrderBy(l => l.DisplayOrder).Select(l => l.ModifierGroupId),
                    variationRules = p.VariationRules.Select(r => new
                    {
                        r.Id,
                        variationId = r.ProductVariationId,
                        r.ModifierGroupId,
                        r.MinSelections,
                        r.MaxSelections,
                        r.FreeAllowance,
                        r.IsRequired,
                        r.IsHidden
                    })
                };
            }).ToList();

            var bestSellerIds = productDtos
                .Where(p => p.salesCount > 0 || p.IsFeatured)
                .OrderByDescending(p => p.salesCount)
                .ThenByDescending(p => p.IsFeatured)
                .Take(10)
                .Select(p => p.Id)
                .ToList();

            return Results.Ok(new
            {
                tenantId = tenant.Id,
                storeTradeName = tenant.TradeName,
                segmentCode = tenant.SegmentCode,
                usesPitakoBranding = false,
                operatingStatus,
                categories = categories
                    .Where(c => c.Status == CatalogItemStatus.Active)
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Description,
                        c.ImageUrl,
                        c.DisplayOrder,
                        c.SegmentTemplateCode,
                        c.RuleNotes,
                        templateKind = (int)c.TemplateKind,
                        templateKindName = c.TemplateKind.ToString(),
                        c.AvailabilityScheduleJson,
                        isScheduledAvailable = c.IsAvailableAt(utcNow),
                        status = c.Status.ToString()
                    }),
                products = productDtos,
                bestSellerIds,
                modifierGroups = modifierGroups
                    .Where(g => g.IsActive)
                    .OrderBy(g => g.DisplayOrder)
                    .Select(g => new
                    {
                        g.Id,
                        g.Name,
                        groupType = g.Type.ToString(),
                        selectionKind = (int)g.SelectionKind,
                        selectionKindName = g.SelectionKind.ToString(),
                        g.MinSelections,
                        g.MaxSelections,
                        g.FreeAllowance,
                        g.IsRequired,
                        countingMode = (int)g.CountingMode,
                        g.CategoryTag,
                        g.DisplayOrder,
                        options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new
                        {
                            o.Id,
                            o.Name,
                            o.Description,
                            additionalPrice = o.AdditionalPrice.Amount,
                            o.MaxQuantity,
                            o.DisplayOrder,
                            o.IsAvailable,
                            o.ReplacesLabel,
                            o.ImageUrl,
                            o.Sku,
                            incompatibleOptionIds = string.IsNullOrWhiteSpace(o.IncompatibleOptionIdsCsv)
                                ? Array.Empty<string>()
                                : o.IncompatibleOptionIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        })
                    }),
                combos = combos.Where(cb => cb.Status == CatalogItemStatus.Active).OrderBy(cb => cb.DisplayOrder).Select(cb => new
                {
                    cb.Id,
                    cb.Name,
                    cb.Description,
                    comboPrice = cb.ComboPrice.Amount,
                    originalPrice = cb.Items.Sum(it => (productMap.TryGetValue(it.ProductId, out var pr) ? pr.BasePrice.Amount : 0m) * it.Quantity),
                    cb.PrimaryImageUrl,
                    cb.IsFeatured,
                    cb.DisplayOrder,
                    items = cb.Items.Select(it => new
                    {
                        it.ProductId,
                        productName = productMap.TryGetValue(it.ProductId, out var pr) ? pr.Name : "Item",
                        it.Quantity,
                        it.VariationId,
                        it.Notes
                    })
                }),
                promotions = activePromos.Select(pr => new
                {
                    pr.Id,
                    pr.Name,
                    discountType = pr.DiscountType.ToString(),
                    pr.DiscountValue,
                    pr.StartsAtUtc,
                    pr.EndsAtUtc,
                    targets = pr.Targets.Select(t => new { targetType = t.TargetType.ToString(), t.TargetId })
                }),
                delivery = deliveryPolicy is null ? null : new
                {
                    deliveryPolicy.IsDeliveryEnabled,
                    deliveryPolicy.AllowPickup,
                    deliveryPolicy.IsTemporarilyPaused,
                    deliveryPolicy.PauseReason,
                    calculationMode = deliveryPolicy.CalculationMode.ToString(),
                    baseFee = deliveryPolicy.BaseFee.Amount,
                    deliveryPolicy.BaseEstimatedMinutes,
                    minimumOrderAmount = deliveryPolicy.MinimumOrderAmount.Amount,
                    freeShippingThreshold = deliveryPolicy.FreeShippingThreshold?.Amount,
                    zones = deliveryPolicy.ZoneRules.Where(z => !z.IsBlocked).Select(z => new { z.Id, z.ZoneName, fee = z.Fee.Amount, z.EstimatedMinutes })
                },
                acai = new
                {
                    cupSizes = acai.CupSizes.Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.VolumeMl,
                        c.Description,
                        basePrice = c.BasePrice.Amount,
                        c.FreeToppingsLimit,
                        c.MinFreeToppings,
                        countingMode = c.CountingMode.ToString(),
                        c.AllowFreeToppings,
                        c.AllowPaidAddons,
                        c.MaxPaidAddonsPerCup,
                        c.DisplayOrder
                    }),
                    addonCategories = (acai.AddonCategories ?? []).Select(cat => new
                    {
                        cat.Id,
                        cat.Name,
                        kind = (int)cat.Kind,
                        cat.IsFreeByDefault,
                        cat.Description,
                        cat.DisplayOrder
                    }),
                    baseFlavors = acai.BaseFlavors.Select(f => new { f.Id, f.Name, extraPrice = f.ExtraPrice.Amount }),
                    freeToppings = acai.FreeToppings.Select(t => new { t.Id, t.Name, t.Description, t.AddonCategoryId, t.CategoryName, t.MaxPortionsPerCup, t.DisplayOrder }),
                    paidAddons = acai.PaidAddons.Select(a => new { a.Id, a.Name, a.Description, a.AddonCategoryId, a.CategoryName, price = a.AdditionalPrice.Amount, a.MaxQuantityPerCup, a.DisplayOrder })
                }
            });
        });

        pub.MapPost("/checkout", async (
            string slug,
            PublicCheckoutReq req,
            ITenantRepository tenantRepo,
            IStoreVisualIdentityRepository identityRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CatalogManagementService catalogSvc,
            ICouponRepository couponRepo,
            IOrderRepository orderRepo,
            OrderManagementService orderSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se suspensa e não pode receber novos pedidos."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            var identity = await identityRepo.FindByTenantIdForPublicViewAsync(tenant.Id, ct);
            if (identity?.IsTemporarilyPaused == true)
            {
                return ApiSecurityGuard.ProblemFromError(Error.Conflict(
                    "Store.TemporarilyPaused",
                    identity.PauseReason ?? "Estabelecimento temporariamente fechado para novos pedidos."));
            }

            var sessionKey = string.IsNullOrWhiteSpace(req.CartSessionKey) ? $"web-{Guid.NewGuid():N}" : req.CartSessionKey.Trim();
            var cartRes = await cartSvc.GetOrCreateCartAsync(sessionKey, req.CustomerPhone ?? req.CustomerName, ct);
            if (cartRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(cartRes.Error);

            foreach (var item in req.Items ?? [])
            {
                var addRes = await cartSvc.AddCatalogProductAsync(sessionKey, item.ProductId, item.VariationId, item.Modifiers, item.Quantity, item.Notes, false, ct);
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

            var activeCart = cartRes.Value;
            if (!string.IsNullOrWhiteSpace(req.CouponCode) && string.IsNullOrWhiteSpace(activeCart.AppliedCouponCode))
            {
                var applyC = await cartSvc.ApplyCouponAsync(sessionKey, req.CouponCode, req.CustomerPhone ?? req.CustomerName, ct);
                if (applyC.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(applyC.Error);
                activeCart = applyC.Value;
            }

            if (!string.IsNullOrWhiteSpace(activeCart.AppliedCouponCode))
            {
                var appliedCoupon = await couponRepo.FindByCodeAsync(activeCart.AppliedCouponCode, ct);
                if (appliedCoupon is not null && appliedCoupon.IsFirstOrderOnly)
                {
                    var customerDigits = new string((req.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray());
                    var customerNameNorm = (req.CustomerName ?? string.Empty).Trim().ToLowerInvariant();
                    var existingOrders = await orderRepo.ListRecentAllAsync(1000, ct);
                    var hasPrevOrder = existingOrders.Any(o =>
                        o.Status != OrderStatus.Cancelled &&
                        ((!string.IsNullOrWhiteSpace(customerDigits) &&
                          new string((o.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray()) == customerDigits) ||
                         (!string.IsNullOrWhiteSpace(customerNameNorm) &&
                          (o.CustomerName ?? string.Empty).Trim().ToLowerInvariant() == customerNameNorm)));

                    if (hasPrevOrder)
                    {
                        return ApiSecurityGuard.ProblemFromError(Error.Conflict(
                            "Coupon.FirstOrderOnly",
                            "O cupom aplicado é válido exclusivamente para o 1º pedido na loja."));
                    }
                }
            }

            var transientActorRes = User.CreateEstablishmentUser(
                tenant.Id,
                "Cardápio Digital",
                $"digital@{tenant.Slug}.local",
                "StorefrontSystemActor!2026",
                EstablishmentRole.Manager);
            if (transientActorRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(transientActorRes.Error);

            var catalogStockItems = activeCart.Items
                .Where(i => i.ItemKind == CartItemKind.CatalogProduct && i.ProductId.HasValue)
                .Select(i => (i.ProductId!.Value, i.VariationId, i.Quantity))
                .ToList();

            var stockPreCheck = await catalogSvc.CheckCatalogStockAvailabilityAsync(catalogStockItems, ct);
            if (stockPreCheck.IsFailure)
                return ApiSecurityGuard.ProblemFromError(stockPreCheck.Error);

            var checkoutRes = await orderSvc.CheckoutCartAsync(
                transientActorRes.Value,
                new CheckoutCartRequest(
                    CartSessionKey: sessionKey,
                    Channel: OrderChannel.OwnDeliveryMenu,
                    FulfillmentMode: req.FulfillmentMode,
                    CustomerName: req.CustomerName,
                    CustomerPhone: req.CustomerPhone,
                    StoreUnitId: null,
                    DeliveryAddressSummary: req.DeliveryAddress,
                    DestinationLatitude: req.DestinationLatitude,
                    DestinationLongitude: req.DestinationLongitude,
                    Neighborhood: req.Neighborhood,
                    ZipCode: req.ZipCode,
                    PrecomputedDistanceKm: req.PrecomputedDistanceKm,
                    IdempotencyKey: req.IdempotencyKey,
                    PaymentMethod: req.PaymentMethod,
                    LgpdConsentAccepted: req.LgpdConsentAccepted,
                    AllowFallbackZeroFreightIfNoPolicy: true),
                ct);

            if (checkoutRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(checkoutRes.Error);

            await catalogSvc.ValidateAndDecrementCatalogStockAsync(catalogStockItems, ct);

            if (req.ClearCartAfterCheckout)
                await cartSvc.ClearCartAsync(sessionKey, ct);

            var order = checkoutRes.Value;
            return Results.Created($"/api/v1/orders/{order.Id}", new
            {
                order.Id,
                order.OrderNumber,
                itemsSubtotal = order.ItemsSubtotal.Amount,
                itemsDiscount = order.ItemsDiscount.Amount,
                netDeliveryFee = order.NetDeliveryFee.Amount,
                totalAmount = order.TotalAmount.Amount,
                paymentMethod = order.PaymentMethod,
                fulfillmentMode = order.FulfillmentMode.ToString(),
                status = order.Status.ToString(),
                order.PlacedAtUtc
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
            IModifierGroupRepository groupRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null)
                return err;

            var categories = await categoryRepo.ListOrderedAsync(ct);
            var products = (await productRepo.ListAllOrderedAsync(ct))
                .Where(p => p.Status != CatalogItemStatus.Archived)
                .ToList();
            var groups = await groupRepo.ListAllAsync(ct);
            return Results.Ok(new
            {
                categories = categories.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    c.ImageUrl,
                    c.DisplayOrder,
                    c.SegmentTemplateCode,
                    c.RuleNotes,
                    templateKind = (int)c.TemplateKind,
                    c.AvailabilityScheduleJson,
                    status = c.Status.ToString()
                }),
                products = products.Select(p => new
                {
                    p.Id,
                    p.CategoryId,
                    p.Name,
                    p.Description,
                    p.Sku,
                    basePrice = p.BasePrice.Amount,
                    promotionalPrice = p.PromotionalPrice?.Amount,
                    p.TrackInventory,
                    p.StockQuantity,
                    p.AutoPauseOnZeroStock,
                    p.ServesPeopleCount,
                    p.DietaryTagsCsv,
                    p.AvailabilityScheduleJson,
                    status = p.Status.ToString(),
                    saleMode = p.SaleMode.ToString(),
                    pricingStrategy = (int)p.PricingStrategy,
                    p.IsFeatured,
                    p.IsNew,
                    p.IsRecommended,
                    p.AllowsNotes,
                    p.PreparationMinutes,
                    p.TagsCsv,
                    p.CustomTemplateKey,
                    p.DisplayOrder,
                    primaryImageUrl = p.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                    images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new { i.Id, i.ImageUrl, i.DisplayOrder, i.IsPrimary }),
                    variations = p.Variations.OrderBy(v => v.DisplayOrder).Select(v => new
                    {
                        v.Id,
                        variationName = v.Name,
                        v.Description,
                        price = v.Price.Amount,
                        costPrice = v.CostPrice.Amount,
                        v.MeasureAmount,
                        v.MeasureUnit,
                        v.MaxFlavors,
                        v.Sku,
                        v.DisplayOrder,
                        v.IsActive,
                        v.TrackInventory,
                        v.StockQuantity
                    }),
                    modifierGroupIds = p.ModifierLinks.OrderBy(l => l.DisplayOrder).Select(l => l.ModifierGroupId),
                    variationRules = p.VariationRules.Select(r => new
                    {
                        r.Id,
                        variationId = r.ProductVariationId,
                        r.ModifierGroupId,
                        r.MinSelections,
                        r.MaxSelections,
                        r.FreeAllowance,
                        r.IsRequired,
                        r.IsHidden
                    })
                }),
                modifierGroups = groups.OrderBy(g => g.DisplayOrder).Select(g => new
                {
                    g.Id,
                    g.Name,
                    groupType = (int)g.Type,
                    selectionKind = (int)g.SelectionKind,
                    g.MinSelections,
                    g.MaxSelections,
                    g.FreeAllowance,
                    g.IsRequired,
                    countingMode = (int)g.CountingMode,
                    g.CategoryTag,
                    g.DisplayOrder,
                    g.IsActive,
                    options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new
                    {
                        o.Id,
                        o.Name,
                        o.Description,
                        additionalPrice = o.AdditionalPrice.Amount,
                        costPrice = o.CostPrice.Amount,
                        o.MaxQuantity,
                        o.DisplayOrder,
                        o.IsAvailable,
                        o.SupplyItemId,
                        o.ConsumptionQuantity,
                        o.ConsumptionUnitCode,
                        o.LossPercent,
                        o.ReplacesLabel,
                        o.IncompatibleOptionIdsCsv,
                        o.ImageUrl,
                        o.Sku
                    })
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

            return ToHttp(await svc.CreateCategoryAsync(user!, req.Name, req.Description, req.ImageUrl, req.DisplayOrder, req.SegmentTemplateCode, req.RuleNotes, ct, req.TemplateKind, req.AvailabilityScheduleJson), StatusCodes.Status201Created);
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

            return ToHttp(await svc.CreateProductAsync(user!, req.CategoryId, req.Name, req.BasePrice, req.Description, req.Sku, req.SaleMode, req.IsFeatured, req.DisplayOrder, req.IsNew, req.IsRecommended, req.PricingStrategy, req.TagsCsv, req.PreparationMinutes, req.CustomTemplateKey, req.AllowsNotes, ct, req.PromotionalPrice, req.TrackInventory, req.StockQuantity, req.AutoPauseOnZeroStock, req.ServesPeopleCount, req.DietaryTagsCsv, req.AvailabilityScheduleJson), StatusCodes.Status201Created);
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

            return ToHttp(await svc.CreateCupSizeAsync(
                user!,
                req.Name,
                req.VolumeMl,
                req.BasePrice,
                req.FreeToppingsLimit,
                req.MinFreeToppings,
                req.CountingMode,
                req.MaxPaidAddonsPerCup,
                null,
                null,
                req.DisplayOrder,
                ct,
                req.Description,
                req.CostPrice,
                req.AllowFreeToppings,
                req.AllowPaidAddons), StatusCodes.Status201Created);
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

            return ToHttp(await svc.CreateFreeToppingAsync(
                user!,
                req.Name,
                req.MaxPortionsPerCup,
                req.DisplayOrder,
                null,
                null,
                null,
                ct,
                req.Description,
                req.CostPrice,
                req.AddonCategoryId,
                req.CategoryName), StatusCodes.Status201Created);
        });

        acai.MapPost("/paid-addons", async (CreateAcaiPaidReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null)
                return err;

            return ToHttp(await svc.CreatePaidAddonAsync(
                user!,
                req.Name,
                req.AdditionalPrice,
                req.MaxQuantityPerCup,
                req.DisplayOrder,
                null,
                null,
                null,
                ct,
                req.Description,
                req.CostPrice,
                req.AddonCategoryId,
                req.CategoryName), StatusCodes.Status201Created);
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

        v1.MapPost("/orders/{id:guid}/status", async (
            Guid id,
            TransitionOrderReq req,
            ApiSecurityGuard guard,
            OrderManagementService svc,
            ITenantRepository tenantRepo,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null)
                return err;

            var res = await svc.TransitionKanbanStatusAsync(user!, id, req.NextStatus, req.Reason ?? "Atualização operacional", ct);
            if (res.IsSuccess)
            {
                var statusLabel = res.Value.Status switch
                {
                    OrderStatus.Accepted => "Pedido Aceito pela loja",
                    OrderStatus.InPreparation => "Em Preparo na cozinha",
                    OrderStatus.Ready => "Pronto para retirada/expedição",
                    OrderStatus.Dispatched => "Saiu para Entrega",
                    OrderStatus.Completed => "Concluído / Entregue",
                    OrderStatus.Cancelled => $"Cancelado ({req.Reason ?? "motivo operacional"})",
                    _ => res.Value.Status.ToString()
                };
                await PitakoStoreSuiteEndpoints.TryNotifyCustomerOrderUpdateAsync(
                    user!.TenantId!.Value,
                    res.Value,
                    $"Atualização do seu pedido #{res.Value.OrderNumber}: {statusLabel}!",
                    tenantRepo,
                    channelRepo,
                    convRepo,
                    clock.UtcNow,
                    ct);
            }
            return ToHttp(res);
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
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se suspensa e não pode receber novos pedidos."));

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
            if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Store.Suspended", "Esta loja encontra-se suspensa e não pode receber novos pedidos."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var callRes = await qrSvc.CallWaiterAsync(token, req.CallType, req.Notes, ct);
            if (callRes.IsFailure && callRes.Error.Code is "QrSession.NotFound" or "QrSession.Inactive" or "QrSession.Expired" or "Tab.AlreadyClosed")
            {
                var resumed = await qrSvc.StartOrResumeByQrTokenAsync(token, cancellationToken: ct);
                if (resumed.IsSuccess)
                    callRes = await qrSvc.CallWaiterAsync(resumed.Value.Session.SessionToken, req.CallType, req.Notes, ct);
            }
            return ToHttp(callRes, StatusCodes.Status201Created);
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
public sealed record PasswordRecoveryReq(string Email, string? StoreSlug = null, string? WebBaseUrl = null);
public sealed record CompletePasswordResetReq(string Token, string NewPassword);
public sealed record SaveEmailSmtpReq(string Host, int Port, bool UseSsl, string Username, string? Password, string FromEmail, string FromName, string PublicWebBaseUrl);
public sealed record CreateMasterPlanReq(string Code, string Name, decimal PriceAmount, string? Description = null, int TrialDays = 14, bool IsUnlimited = false, int? MaxEstablishments = null, int? MaxUnits = null, int? MaxMarketplaces = null, int? MaxUsers = null, int? MaxDevices = null, int? MaxSessions = null, int? MaxMonthlyOrders = null, IReadOnlyList<string>? EnabledModules = null, bool? IsActive = null);
public sealed record CreateCustomerSubReq(string CustomerName, string DocumentNumber, string BillingEmail, string? Phone, Guid PlanId, bool StartWithTrial = false);
public sealed record UpdateMasterCustomerReq(string CustomerName, string DocumentNumber, string BillingEmail, string? Phone, CustomerAccountStatus Status, Guid? PlanId = null);
public sealed record UpdateEstablishmentMasterReq(string LegalName, string TradeName, string Slug, string Cnpj, string SegmentCode, TenantStatus Status);
public sealed record UpsertSegmentReq(string Code, string DisplayName, bool SupportsAcaiBuilder, bool SupportsWeightScale, bool SupportsTableService, bool SupportsFractionalItems, bool SupportsPizzaBuilder = false, bool SupportsBurgerBuilder = false, bool SupportsSnackBuilder = false);

internal static class MasterSegmentCatalogStore
{
    private const string CustomKey = "master.custom_segments";
    private const string HiddenKey = "master.hidden_segments";
    private static readonly SemaphoreSlim SegmentGate = new(1, 1);

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
        bool createOnly = false,
        CancellationToken ct = default)
    {
        var check = BusinessSegmentCatalog.Resolve(req.Code);
        if (check.IsFailure)
            return ApiSecurityGuard.ProblemFromError(check.Error);
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return ApiSecurityGuard.ProblemFromError(Error.Validation("Segment.DisplayNameRequired", "Nome da modalidade é obrigatório."));

        var normalizedCode = BusinessSegmentCatalog.Normalize(req.Code);
        var trimmedName = req.DisplayName.Trim();
        await SegmentGate.WaitAsync(ct);
        try
        {
            var effective = await GetEffectiveSegmentsAsync(settingRepo, ct);
            if (createOnly)
            {
                if (effective.Any(s =>
                    s.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase) ||
                    s.DisplayName.Trim().Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
                {
                    return ApiSecurityGuard.ProblemFromError(
                        Error.Conflict("Segment.AlreadyExists", $"A modalidade '{trimmedName}' já está cadastrada."));
                }
            }
            else if (effective.Any(s =>
                !s.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase) &&
                s.DisplayName.Trim().Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                return ApiSecurityGuard.ProblemFromError(
                    Error.Conflict("Segment.AlreadyExists", $"Já existe outra modalidade com o nome '{trimmedName}'."));
            }

            var customSetting = await settingRepo.FindByKeyAsync(CustomKey, ct);
            var customList = string.IsNullOrWhiteSpace(customSetting?.Value)
                ? []
                : (JsonSerializer.Deserialize<List<BusinessSegmentProfile>>(customSetting.Value) ?? []);

            customList.RemoveAll(s => s.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
            var saved = new BusinessSegmentProfile(
                normalizedCode,
                trimmedName,
                req.SupportsAcaiBuilder,
                req.SupportsWeightScale,
                req.SupportsTableService,
                req.SupportsFractionalItems,
                req.SupportsPizzaBuilder,
                req.SupportsBurgerBuilder,
                req.SupportsSnackBuilder);
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
        finally
        {
            SegmentGate.Release();
        }
    }

    public static async Task<IResult> DeleteSegmentAsync(
        User actor,
        string code,
        IGlobalSettingRepository settingRepo,
        MasterOperationsAndDashboardService ops,
        CancellationToken ct)
    {
        var normalized = BusinessSegmentCatalog.Normalize(code);
        await SegmentGate.WaitAsync(ct);
        try
        {
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
        finally
        {
            SegmentGate.Release();
        }
    }
}
public sealed record CreateStoreUserReq(string FullName, string Email, string Password, EstablishmentRole Role);
public sealed record PublicCheckoutItemReq(Guid ProductId, int Quantity = 1, string? Notes = null, Guid? VariationId = null, IReadOnlyList<CatalogModifierSelection>? Modifiers = null);
public sealed record PublicCheckoutReq(string CustomerName, string? CustomerPhone, FulfillmentMode FulfillmentMode, string? DeliveryAddress = null, string? CartSessionKey = null, string? IdempotencyKey = null, IReadOnlyList<PublicCheckoutItemReq>? Items = null, IReadOnlyList<AcaiCupBuildRequest>? AcaiCups = null, string? Neighborhood = null, string? ZipCode = null, double? DestinationLatitude = null, double? DestinationLongitude = null, decimal? PrecomputedDistanceKm = null, string? PaymentMethod = null, bool LgpdConsentAccepted = true, bool ClearCartAfterCheckout = true, string? CouponCode = null);
public sealed record CreateCategoryReq(string Name, string? Description = null, string? ImageUrl = null, int DisplayOrder = 0, string? SegmentTemplateCode = null, string? RuleNotes = null, CategoryTemplateKind TemplateKind = CategoryTemplateKind.Standard, string? AvailabilityScheduleJson = null);
public sealed record CreateProductReq(Guid CategoryId, string Name, decimal BasePrice, string? Description = null, string? Sku = null, ProductSaleMode SaleMode = ProductSaleMode.Unit, bool IsFeatured = false, int DisplayOrder = 0, bool IsNew = false, bool IsRecommended = false, ProductPricingStrategy PricingStrategy = ProductPricingStrategy.FixedPlusAddons, string? TagsCsv = null, int? PreparationMinutes = null, string? CustomTemplateKey = null, bool AllowsNotes = true, decimal? PromotionalPrice = null, bool TrackInventory = false, int StockQuantity = 0, bool AutoPauseOnZeroStock = false, int? ServesPeopleCount = null, string? DietaryTagsCsv = null, string? AvailabilityScheduleJson = null);
public sealed record CreateAcaiCupReq(string Name, int VolumeMl, decimal BasePrice, int FreeToppingsLimit, int MinFreeToppings = 0, string? Description = null, decimal CostPrice = 0m, bool AllowFreeToppings = true, bool AllowPaidAddons = true, int? MaxPaidAddonsPerCup = null, AcaiFreeCountingMode CountingMode = AcaiFreeCountingMode.ByPortion, int DisplayOrder = 0);
public sealed record CreateAcaiFlavorReq(string Name, decimal ExtraPrice = 0m);
public sealed record CreateAcaiFreeReq(string Name, int MaxPortionsPerCup = 1, string? Description = null, decimal CostPrice = 0m, Guid? AddonCategoryId = null, string? CategoryName = null, int DisplayOrder = 0);
public sealed record CreateAcaiPaidReq(string Name, decimal AdditionalPrice, int MaxQuantityPerCup = 3, string? Description = null, decimal CostPrice = 0m, Guid? AddonCategoryId = null, string? CategoryName = null, int DisplayOrder = 0);
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
