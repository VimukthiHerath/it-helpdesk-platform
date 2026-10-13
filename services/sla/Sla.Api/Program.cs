using Azure.Monitor.OpenTelemetry.AspNetCore;
using Sla.Api.Data;
using Microsoft.EntityFrameworkCore;
using Sla.Api.Consumers;
using Sla.Api.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Application Insights (via OpenTelemetry). Off unless APPLICATIONINSIGHTS_CONNECTION_STRING is set,
// so local runs and tests send nothing. Health probes are left out so they don't cost ingestion.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
        o.SamplingRatio = builder.Configuration.GetValue("APPINSIGHTS_SAMPLING_RATIO", 1.0f));
    builder.Services.Configure<OpenTelemetry.Instrumentation.AspNetCore.AspNetCoreTraceInstrumentationOptions>(o =>
        o.Filter = ctx => !ctx.Request.Path.Value!.EndsWith("/health", StringComparison.OrdinalIgnoreCase));
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

var jwtJwksUri = builder.Configuration["Jwt:JwksUri"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(jwtJwksUri))
        {
            options.MetadataAddress = jwtJwksUri.EndsWith("/.well-known/openid-configuration")
                ? jwtJwksUri
                : jwtJwksUri.Replace("/.well-known/jwks.json", "/.well-known/openid-configuration");
            options.RequireHttpsMetadata = false;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"]
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<SlaDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

// One long-lived Kafka producer for the process, instead of building a new
// one on every SlaBreachMonitorService tick.
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var bootstrapServers = sp.GetRequiredService<IConfiguration>()["Kafka:BootstrapServers"] ?? "localhost:9092";
    return new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
});

builder.Services.AddScoped<SlaBreachDetectionService>();
builder.Services.AddScoped<SlaRecordCreationService>();

builder.Services.AddHostedService<TicketCreatedConsumer>();
builder.Services.AddHostedService<TicketResolvedConsumer>();
builder.Services.AddHostedService<SlaBreachMonitorService>();

var app = builder.Build();

// Automatically apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SlaDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "SLA" }));
app.Run();

public partial class Program { }
