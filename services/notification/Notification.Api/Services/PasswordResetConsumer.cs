using ItHelpdesk.Observability;
using System.Text.Json;
using Confluent.Kafka;
using Notification.Api.DTO;

namespace Notification.Api.Services;

// Consumes password-reset-requested events published by Auth.Api and emails the
// reset link. Follows the same startup and commit pattern as TicketCreatedConsumer.
public sealed class PasswordResetConsumer : BackgroundService
{
    private const string ConsumerGroupId = "notification-workers";
    private const string EventType = "PasswordResetRequested";

    private readonly IConfiguration _configuration;
    private readonly ILogger<PasswordResetConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public PasswordResetConsumer(
        IConfiguration configuration,
        ILogger<PasswordResetConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // Consume() blocks, so run it on a background thread to let the host start
    // even when Kafka is not reachable yet (see TicketCreatedConsumer).
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private async Task RunConsumerLoop(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _configuration["Kafka:PasswordResetTopic"] ?? "password-reset-requested";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            // The topic is created by the first reset request. Do not treat a
            // missing topic as an error while waiting for it.
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Password reset consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
            topic, ConsumerGroupId, bootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    using var traceActivity = KafkaTracing.StartConsume(result);

                    if (string.IsNullOrWhiteSpace(result.Message.Value))
                    {
                        _logger.LogWarning(
                            "Received an empty PasswordResetRequested message. Partition={Partition}, Offset={Offset}",
                            result.Partition, result.Offset);
                        continue;
                    }

                    var resetEvent = JsonSerializer.Deserialize<PasswordResetRequestedEvent>(
                        result.Message.Value,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));

                    if (resetEvent is null)
                    {
                        _logger.LogWarning(
                            "Received an invalid PasswordResetRequested event. Partition={Partition}, Offset={Offset}",
                            result.Partition, result.Offset);
                        continue;
                    }

                    var eventKey = $"{EventType}-{result.Partition.Value}-{result.Offset.Value}";

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<PasswordResetNotificationService>();
                        await service.ProcessAsync(resetEvent, eventKey, stoppingToken);

                        consumer.Commit(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error processing event from topic {Topic} at offset {Offset}. Swallowing to prevent crash loop.",
                            result.Topic, result.Offset);
                    }
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Kafka consume error: {Reason}", exception.Error.Reason);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(exception, "Received malformed PasswordResetRequested event");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Password reset Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }
}
