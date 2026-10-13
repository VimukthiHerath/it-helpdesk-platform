using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Auth.Api.Configuration;
using Auth.Api.Data;
using Auth.Api.Services;

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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("AuthDb"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("AuthDb"))));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpClient("AssignmentApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AssignmentApi:BaseUrl"]!);
});
builder.Services.AddScoped<IAssignmentRotationClient, AssignmentRotationClient>();
builder.Services.AddSingleton<IRsaKeyService, RsaKeyService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<IPasswordResetPublisher, KafkaPasswordResetPublisher>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration["cors:FrontendUrl"]!)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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

var jwtSetting = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

// Build a temporary service provider to retrieve IRsaKeyService for signing/validation key
var rsaKeyService = builder.Services.BuildServiceProvider().GetRequiredService<IRsaKeyService>();

var rsaSigningKey = rsaKeyService.GetSigningKey();
var legacySymmetricKey = !string.IsNullOrEmpty(jwtSetting?.Key) 
    ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSetting.Key)) 
    : null;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    var validKeys = new List<SecurityKey> { rsaSigningKey };
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
        ValidIssuer = jwtSetting?.Issuer ?? "AuthApi",
        ValidAudience = jwtSetting?.Audience ?? "ItHelpdeskClient",
        IssuerSigningKeys = validKeys
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

            // Extract User ID and Stamp from incoming token
            var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            var tokenStamp = context.Principal?.FindFirst("SecurityStamp")?.Value;

            if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(tokenStamp))
            {
                context.Fail("This token is missing user identity or session information.");
                return;
            }

            // Look up the current user in the database
            var user = await dbContext.Users.FindAsync(userId);

            // If the user is deleted, inactive, or the security stamp doesn't match, revoke access
            if (user == null || !user.IsActive || user.SecurityStamp != tokenStamp)
            {
                context.Fail("This token is no longer valid due to a password change or session revocation.");
            }
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            message = "An unexpected error occurred. Please try again later."
        });
    });
});

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Auth" }));
// Also exposed under the controller base path so it's reachable through the
// WSO2 gateway, which forwards every "/auth/v1/*" request to {endpoint}/api/Auth/*
// and has no way to special-case a resource onto the service root instead.
app.MapGet("/api/Auth/health", () => Results.Ok(new { status = "healthy", service = "Auth" }));

app.Run();

public partial class Program { }
