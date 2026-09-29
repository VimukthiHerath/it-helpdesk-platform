using Sla.Api.Data;
using Microsoft.EntityFrameworkCore;
using Sla.Api.Services;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "SLA" }));
app.Run();