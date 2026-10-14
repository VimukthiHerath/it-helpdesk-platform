using Azure.Monitor.OpenTelemetry.AspNetCore;
using Assignment.Api.Configuration;
using Assignment.Api.Data;
using Assignment.Api.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Application Insights (via OpenTelemetry). Off unless APPLICATIONINSIGHTS_CONNECTION_STRING is set,
// so local runs and tests send nothing. Health probes are left out so they don't cost ingestion.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(t => t.AddSource(ItHelpdesk.Observability.KafkaTracing.SourceName))
        .UseAzureMonitor(o => o.SamplingRatio = builder.Configuration.GetValue("APPINSIGHTS_SAMPLING_RATIO", 1.0f));
    builder.Services.Configure<OpenTelemetry.Instrumentation.AspNetCore.AspNetCoreTraceInstrumentationOptions>(o =>
        o.Filter = ctx => !ctx.Request.Path.Value!.EndsWith("/health", StringComparison.OrdinalIgnoreCase));
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("AssignmentDb"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("AssignmentDb"))));

builder.Services.AddSingleton<IProducer<string, string>>(_ =>
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        Acks = Acks.All
    };

    return new ProducerBuilder<string, string>(producerConfig).Build();
});

builder.Services.AddScoped<RoundRobinAssignmentService>();
builder.Services.AddHostedService<TicketCreatedConsumer>();
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration["cors:FrontendUrl"]!)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Validates tokens issued by Auth.Api - same Key/Issuer/Audience as every
// other service, so a token minted at login works here without calling Auth.
var jwtSetting = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

var legacySymmetricKey = !string.IsNullOrEmpty(jwtSetting?.Key)
    ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSetting.Key))
    : null;

builder.Services.AddHttpClient();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    if (!string.IsNullOrEmpty(jwtSetting?.JwksUri))
    {
        options.MetadataAddress = jwtSetting.JwksUri.EndsWith("/.well-known/openid-configuration") 
            ? jwtSetting.JwksUri 
            : jwtSetting.JwksUri.Replace("/.well-known/jwks.json", "/.well-known/openid-configuration");
        options.RequireHttpsMetadata = false;
    }

    var validKeys = new List<SecurityKey>();
    if (legacySymmetricKey != null)
    {
        validKeys.Add(legacySymmetricKey);
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = jwtSetting?.Issuer,
        ValidAudience = jwtSetting?.Audience,
        IssuerSigningKeys = validKeys.Count > 0 ? validKeys : null
    };
});

builder.Services.AddAuthorization();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by your token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

var kafkaProducer = app.Services.GetRequiredService<IProducer<string, string>>();
app.Lifetime.ApplicationStopping.Register(() => kafkaProducer.Flush(TimeSpan.FromSeconds(10)));

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Assignment" }));
// Also exposed under the controller base path so it's reachable through the
// WSO2 gateway, which forwards every "/assignment/v1/*" request to
// {endpoint}/api/Assignments/* and has no way to special-case a resource onto
// the service root instead.
app.MapGet("/api/Assignments/health", () => Results.Ok(new { status = "healthy", service = "Assignment" }));
app.MapControllers();
app.Run();

public partial class Program { }