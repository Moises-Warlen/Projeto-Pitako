using System.Data.Common;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Pitako.Api.Bootstrap;
using Pitako.Api.Composition;
using Pitako.Api.Endpoints;
using Pitako.Api.Realtime;
using Pitako.Api.Security;

namespace Pitako.Api;

public static class PitakoApiHostBuilder
{
    public static WebApplication BuildWebApplication(
        string[] args,
        Action<IServiceCollection>? overrideServices = null,
        DbConnection? sharedSqliteConnection = null,
        string? listenUrl = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (!string.IsNullOrWhiteSpace(listenUrl))
            builder.WebHost.UseUrls(listenUrl);

        builder.Services.AddProblemDetails();
        builder.Services.AddSignalR();

        var allowedOrigins = builder.Configuration.GetSection("Security:AllowedCorsOrigins").Get<string[]>()
            ?? ["http://localhost:3000"];

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("PitakoCors", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("auth-login", opt =>
            {
                opt.PermitLimit = 30;
                opt.Window = TimeSpan.FromMinutes(1);
                opt.QueueLimit = 0;
            });
        });

        builder.Services.AddPitakoModularMonolith(builder.Configuration, builder.Environment, sharedSqliteConnection);
        overrideServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.UseMiddleware<PitakoSecurityHeadersMiddleware>();
        app.UseMiddleware<PitakoExceptionHandlingMiddleware>();
        app.UseCors("PitakoCors");
        app.UseRateLimiter();

        app.MapPitakoV1Endpoints();
        app.MapHub<PitakoRealtimeHub>("/hubs/realtime");

        return app;
    }
}

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var app = PitakoApiHostBuilder.BuildWebApplication(args);

        var provider = app.Configuration["DatabaseProvider"] ?? "PostgreSql";
        var useSqlite = string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase);

        await app.Services.EnsureDatabaseInitializedAsync(useSqlite);

        if (app.Configuration.GetValue<bool>("SeedDemoData"))
            await PitakoDevDemoSeeder.SeedIfNeededAsync(app.Services);

        await app.RunAsync();
    }
}
