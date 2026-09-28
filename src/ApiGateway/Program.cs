using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("GatewayPolicy", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            // Explicitly expose Authorization so downstream JWT flows are visible
            .WithExposedHeaders("Authorization", "WWW-Authenticate");
    });
});

// ── YARP Reverse Proxy ────────────────────────────────────────────────────────
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("gateway-self", () => HealthCheckResult.Healthy("API Gateway is running."));

// ── Minimal API metadata ──────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseCors("GatewayPolicy");

// Strip and forward Authorization: Bearer <token> to downstream services.
// YARP copies all request headers by default (RequestHeadersCopy: true in config),
// so we only need to ensure the header is not stripped by any earlier middleware.
app.Use(async (context, next) =>
{
    // If Authorization header is present, ensure it flows through.
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        // YARP will copy it automatically; this middleware is a no-op guard.
        context.Request.Headers["X-Forwarded-Authorization"] =
            context.Request.Headers["Authorization"].ToString();
    }
    await next(context);
});

// ── Gateway /health endpoint ──────────────────────────────────────────────────
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        // Retrieve YARP route count from DI
        var proxyConfig = context.RequestServices.GetRequiredService<IProxyConfigProvider>();
        var config = proxyConfig.GetConfig();
        int routeCount   = config.Routes.Count;
        int clusterCount = config.Clusters.Count;

        var result = new
        {
            status    = report.Status.ToString(),
            gateway   = "IT Helpdesk API Gateway",
            version   = "1.0.0",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            routes    = routeCount,
            clusters  = clusterCount,
            checks    = report.Entries.Select(e => new
            {
                name     = e.Key,
                status   = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds
            })
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await context.Response.WriteAsync(json, Encoding.UTF8);
    }
});

// ── YARP reverse proxy middleware ─────────────────────────────────────────────
app.MapReverseProxy();

app.Run();
