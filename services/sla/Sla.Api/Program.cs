using Microsoft.EntityFrameworkCore;
using Sla.Api.Data;
using Sla.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();

// Register DbContext with the connection string from appsettings.json
var connectionString = builder.Configuration.GetConnectionString("SlaDb");
builder.Services.AddDbContext<SlaDbContext>(options =>
{
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    );
});

builder.Services.AddHostedService<TicketCreatedConsumer>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "SLA" }));
app.Run();