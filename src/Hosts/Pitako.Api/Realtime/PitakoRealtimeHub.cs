using Microsoft.AspNetCore.SignalR;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;

namespace Pitako.Api.Realtime;

/// <summary>
/// Hub SignalR em tempo real isolado por tenant (Kanban de pedidos, chamados de garçom, GPS de entregas e inbox WhatsApp).
/// </summary>
public sealed class PitakoRealtimeHub(AuthenticationService authService) : Hub
{
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:N}";
    public static string SaasGroup => "saas:master";

    public override async Task OnConnectedAsync()
    {
        var token = ResolveBearerToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            Context.Abort();
            return;
        }

        var userRes = await authService.ValidateActiveAccessTokenAsync(token, Context.ConnectionAborted);
        if (userRes.IsFailure)
        {
            Context.Abort();
            return;
        }

        var user = userRes.Value;
        Context.Items["User"] = user;

        if (user.Scope == UserScope.Saas)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SaasGroup, Context.ConnectionAborted);
        }
        else if (user.TenantId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(user.TenantId.Value), Context.ConnectionAborted);
        }
        else
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public Task<string> PingTenantChannel()
    {
        if (Context.Items["User"] is not User user)
            throw new HubException("Sessão não autenticada.");

        return Task.FromResult(user.Scope == UserScope.Saas ? SaasGroup : TenantGroup(user.TenantId!.Value));
    }

    private string? ResolveBearerToken()
    {
        var http = Context.GetHttpContext();
        if (http is null)
            return null;

        var authHeader = http.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        var qsToken = http.Request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(qsToken) ? null : qsToken.Trim();
    }
}
