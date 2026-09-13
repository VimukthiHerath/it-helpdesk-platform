using Assignment.Api.Data;
using Assignment.Api.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

var app = builder.Build();

var kafkaProducer = app.Services.GetRequiredService<IProducer<string, string>>();
app.Lifetime.ApplicationStopping.Register(() => kafkaProducer.Flush(TimeSpan.FromSeconds(10)));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Assignment" }));
app.Run();