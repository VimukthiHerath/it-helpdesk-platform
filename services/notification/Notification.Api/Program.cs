using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.Services;

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

// Register the idempotency database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Register the real Gmail SMTP email service
builder.Services.AddScoped<IEmailService, EmailService>();

// Register IHttpClientFactory so consumers can call Auth.Api to resolve user emails
builder.Services.AddHttpClient();

builder.Services.AddScoped<IUserEmailResolver, AuthApiUserEmailResolver>();
builder.Services.AddScoped<IAdminEmailResolver, AuthApiAdminEmailResolver>();
builder.Services.AddScoped<TicketCreatedNotificationService>();
builder.Services.AddScoped<SlaBreachedNotificationService>();
builder.Services.AddScoped<PasswordResetNotificationService>();

builder.Services.AddHostedService<TicketCreatedConsumer>();
builder.Services.AddHostedService<SlaBreachedConsumer>();
builder.Services.AddHostedService<TicketAssignedConsumer>();
builder.Services.AddHostedService<PasswordResetConsumer>();

var app = builder.Build();

// Auto-apply any pending migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Notification" }));
app.Run();
