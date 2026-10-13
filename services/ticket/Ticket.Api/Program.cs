using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Ticket.Api.Configuration;
using Ticket.Api.Data;
using Ticket.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// [ApiController] auto-returns its own ValidationProblem shape (an "errors"
// dictionary, no top-level "message") the instant ModelState is invalid -
// before the action body ever runs. Suppressing that lets CreateTicket's own
// ModelState check below actually execute and shape the response itself.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddSingleton<IProducer<string, string>>(_ =>
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        Acks = Acks.All
    };

    return new ProducerBuilder<string, string>(producerConfig).Build();
});

var connectionString = builder.Configuration.GetConnectionString("TicketDb");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)));

builder.Services.AddDbContext<TicketDbContext>(options =>
{
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    );
});

builder.Services.AddScoped<TicketAssignmentSyncService>();
builder.Services.AddHostedService<TicketAssignedConsumer>();
builder.Services.AddScoped<TicketStatusService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration["cors:FrontendUrl"]!)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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
        // Set metadata address or use JWKS configuration manager
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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

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

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Ticket" }));
// Also exposed under the controller base path so it's reachable through the
// WSO2 gateway, which forwards every "/ticket/v1/*" request to {endpoint}/api/Ticket/*
// and has no way to special-case a resource onto the service root instead.
app.MapGet("/api/Ticket/health", () => Results.Ok(new { status = "healthy", service = "Ticket" }));
app.MapControllers();
app.Run();

public partial class Program { }