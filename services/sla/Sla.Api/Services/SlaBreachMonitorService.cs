using System.Text.Json;
using Confluent.Kafka;
using Sla.Api.Data;
using Sla.Api.DTO;

namespace Sla.Api.Services;

public sealed class SlaBreachMonitorService : BackgroundService
{
    private readonly ILogger<SlaBreachMonitorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public SlaBreachMonitorService(
        ILogger<SlaBreachMonitorService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SLA Breach Monitor Service starting.");
        
        // Wait 10 seconds before the first run so the app has time to start completely.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Run exactly every 1 minute
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckForBreachesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SLA Breach Monitor Service is stopping.");
        }
    }

    private async Task CheckForBreachesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Running SLA breach check at {Time}", DateTime.UtcNow);

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _configuration["Kafka:SlaBreachedTopic"] ?? "sla-breached";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlaDbContext>();

        // 1. Find all active tickets whose deadline has passed
        var breachedTickets = db.TicketSlas
            .Where(t => t.Status == "Active" && t.DeadlineUtc <= DateTime.UtcNow)
            .ToList();

        if (!breachedTickets.Any())
        {
            return;
        }

        _logger.LogWarning("Found {Count} breached tickets!", breachedTickets.Count);

        // 2. Setup Kafka Producer
        var config = new ProducerConfig { BootstrapServers = bootstrapServers };
        using var producer = new ProducerBuilder<string, string>(config).Build();

        // 3. Process each breached ticket
        foreach (var ticket in breachedTickets)
        {
            var eventPayload = new SlaBreachedEvent
            {
                TicketId = ticket.TicketId,
                OriginalDeadlineUtc = ticket.DeadlineUtc,
                BreachedAtUtc = DateTime.UtcNow
            };

            var messageString = JsonSerializer.Serialize(eventPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            try
            {
                // Send the Kafka message
                var result = await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = ticket.TicketId.ToString(),
                    Value = messageString
                }, stoppingToken);

                _logger.LogInformation("Published SlaBreached event for Ticket {TicketId}. Partition: {Partition}, Offset: {Offset}",
                    ticket.TicketId, result.Partition, result.Offset);

                // 4. Update the database Status so we don't alarm twice
                ticket.Status = "Breached";
            }
            catch (ProduceException<string, string> ex)
            {
                // If Kafka fails, we log it and continue to the next ticket. 
                // We do NOT update the Status for this ticket in the DB, so it will be retried next minute!
                _logger.LogError(ex, "Failed to publish SLA breach for Ticket {TicketId}. Reason: {Reason}", ticket.TicketId, ex.Error.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing SLA breach for Ticket {TicketId}.", ticket.TicketId);
            }
        }

        // 5. Save all successful status updates
        await db.SaveChangesAsync(stoppingToken);
    }
}
