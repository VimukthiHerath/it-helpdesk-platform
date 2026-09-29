using System.Text.Json;
using Confluent.Kafka;

namespace Notification.Api.Services;

public sealed class SlaBreachedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "notification-workers-sla";
    private const string EventType = "SlaBreached";

    private readonly IConfiguration _configuration;
    private readonly ILogger<SlaBreachedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public SlaBreachedConsumer(
        IConfiguration configuration,
        ILogger<SlaBreachedConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private async Task RunConsumerLoop(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _configuration["Kafka:SlaBreachedTopic"] ?? "sla-breached";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Notification SLA Breached consumer started. Topic={Topic}, GroupId={GroupId}",
            topic,
            ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    if (string.IsNullOrWhiteSpace(result.Message.Value))
                    {
                        continue;
                    }

                    var breachedEvent = JsonSerializer.Deserialize<SlaBreachedEvent>(
                        result.Message.Value,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));

                    if (breachedEvent is null)
                    {
                        continue;
                    }

                    // AC2: Idempotency check ensures email is sent exactly once per breach
                    var eventKey = $"{EventType}-{breachedEvent.TicketId}";

                    using var scope = _scopeFactory.CreateScope();
                    var notificationService = scope.ServiceProvider.GetRequiredService<SlaBreachedNotificationService>();
                    await notificationService.ProcessAsync(breachedEvent, eventKey, stoppingToken);
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Kafka consume error: {Reason}", exception.Error.Reason);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected error processing SLA Breach event");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Notification SLA Breached consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }

}

public class SlaBreachedEvent
{
    public int TicketId { get; set; }
    public DateTime OriginalDeadlineUtc { get; set; }
    public DateTime BreachedAtUtc { get; set; }
}
