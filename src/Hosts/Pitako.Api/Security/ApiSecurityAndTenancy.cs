using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Api.Security;

/// <summary>
/// Cabeçalhos de segurança HTTP e Correlation-ID obrigatórios em todas as respostas da API.
/// </summary>
public sealed class PitakoSecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = context.TraceIdentifier;

        context.Response.Headers["X-Correlation-ID"] = correlationId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-XSS-Protection"] = "0";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";

        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.Headers["Cache-Control"] = "no-store";

        await next(context);
    }
}

/// <summary>
/// Middleware global de exceções emitindo ProblemDetails RFC 7807 sem vazar stack trace ou segredos.
/// </summary>
public sealed class PitakoExceptionHandlingMiddleware(RequestDelegate next, ILogger<PitakoExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (CrossTenantAccessException ex)
        {
            logger.LogWarning(ex, "Bloqueio cross-tenant detectado na requisição {Path}.", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Acesso cross-tenant bloqueado.",
                "MultiTenancy.CrossTenantBlocked",
                ex.Message);
        }
        catch (JsonException)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Payload JSON inválido.",
                "Http.InvalidJson",
                "O corpo da requisição contém JSON malformado.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro interno não tratado na requisição {Path}.", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Erro interno no servidor.",
                "Server.UnexpectedError",
                "Ocorreu uma falha inesperada ao processar a solicitação.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string errorCode,
        string detail)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://pitako.local/errors/{errorCode}",
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = errorCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(problem);
    }
}

/// <summary>
/// Resolve autenticação via JWT no servidor, preenche ITenantContext exclusivamente a partir do token
/// (bloqueando cabeçalhos X-Tenant-Id forjados) e valida habilitação de módulo no plano do cliente.
/// </summary>
public sealed class ApiSecurityGuard(
    AuthenticationService authService,
    ITenantContextSetter tenantContextSetter,
    ITenantRepository tenantRepository,
    PlanEntitlementEnforcementService planEnforcement)
{
    public async Task<(User? User, IResult? ErrorResult)> RequireAuthenticatedUserAsync(
        HttpContext http,
        UserScope? requiredScope = null,
        string? requiredModuleCode = null,
        CancellationToken ct = default)
    {
        var authHeader = http.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (null, ProblemFromError(Error.Unauthorized("Auth.TokenMissing", "Token Bearer obrigatório.")));

        var rawToken = authHeader["Bearer ".Length..].Trim();
        var userResult = await authService.ValidateActiveAccessTokenAsync(rawToken, ct);
        if (userResult.IsFailure)
            return (null, ProblemFromError(userResult.Error));

        var user = userResult.Value;
        if (requiredScope.HasValue && user.Scope != requiredScope.Value)
        {
            return (null, ProblemFromError(Error.Forbidden(
                "Auth.ScopeMismatch",
                requiredScope.Value == UserScope.Saas
                    ? "Endpoint restrito a operadores administrativos SaaS."
                    : "Endpoint restrito a usuários de estabelecimentos.")));
        }

        // Nunca aceita X-Tenant-Id forjado para trocar o escopo resolvido no token JWT.
        if (http.Request.Headers.TryGetValue("X-Tenant-Id", out var forgedHeader) &&
            Guid.TryParse(forgedHeader.ToString(), out var headerTenantId))
        {
            if (user.Scope == UserScope.Establishment && user.TenantId != headerTenantId)
                throw new CrossTenantAccessException(user.TenantId, headerTenantId, "Cabeçalho X-Tenant-Id divergente da sessão autenticada.");
        }

        if (user.Scope == UserScope.Saas)
        {
            tenantContextSetter.SetMasterScope();
            return (user, null);
        }

        if (!user.TenantId.HasValue)
            return (null, ProblemFromError(Error.Forbidden("Auth.TenantMissing", "Usuário de estabelecimento sem vínculo de loja.")));

        tenantContextSetter.SetTenant(user.TenantId.Value, TenantResolutionSource.AuthenticatedSession);

        var tenant = await tenantRepository.FindByIdAsync(user.TenantId.Value, ct);
        if (tenant is null)
            return (null, ProblemFromError(Error.NotFound("Tenant.NotFound", "Estabelecimento não localizado.")));

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
        {
            return (null, ProblemFromError(Error.Forbidden(
                "Tenant.Suspended",
                "Estabelecimento suspenso ou bloqueado. Regularize a assinatura ou contate a plataforma.")));
        }

        if (tenant.CustomerAccountId.HasValue)
        {
            var accessRes = !string.IsNullOrWhiteSpace(requiredModuleCode)
                ? await planEnforcement.CheckModuleAccessAsync(tenant.CustomerAccountId.Value, requiredModuleCode, ct)
                : await planEnforcement.EnsureSubscriptionOperationalAsync(tenant.CustomerAccountId.Value, ct);
            if (accessRes.IsFailure)
                return (null, ProblemFromError(accessRes.Error));
        }

        return (user, null);
    }

    public static IResult ProblemFromError(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.LimitExceeded => StatusCodes.Status402PaymentRequired,
            _ when error.Code.Contains("Unavailable", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: status,
            type: $"https://pitako.local/errors/{error.Code}");
    }
}
