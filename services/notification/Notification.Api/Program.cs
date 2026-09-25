using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.Services;

var builder = WebApplication.CreateBuilder(args);
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

builder.Services.AddHostedService<TicketCreatedConsumer>();

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
