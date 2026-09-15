using System.Text.Json;
using Ticket.Api.DTO;
using Confluent.Kafka;

namespace Ticket.Api.Services;

public sealed class TicketAssignedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "ticket-workers";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketAssignedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketAssignedConsumer(
        IConfiguration configuration,
        ILogger<TicketAssignedConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // Same background-thread fix as Assignment.Api's TicketCreatedConsumer:
    // Consume() blocks, so running the loop inline would block host startup
    // (and Kestrel binding) until Kafka is reachable.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoopAsync(stoppingToken), stoppingToken);
    }

    private async Task RunConsumerLoopAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers =
            _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var topic =
            _configuration["Kafka:TicketAssignedTopic"] ?? "ticket-assigned";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(
            consumerConfig).Build();

        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Ticket consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
            topic,
            ConsumerGroupId,
            bootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    if (string.IsNullOrWhiteSpace(result.Message.Value))
                    {
                        _logger.LogWarning(
                            "Received an empty TicketAssigned message. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    var assignedEvent =
                        JsonSerializer.Deserialize<TicketAssignedEvent>(
                            result.Message.Value,
                            new JsonSerializerOptions(
                                JsonSerializerDefaults.Web));

                    if (assignedEvent is null)
                    {
                        _logger.LogWarning(
                            "Received an invalid TicketAssigned event. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    _logger.LogInformation(
                        "Received TicketAssigned event. " +
                        "TicketId={TicketId}, AgentUserId={AgentUserId}, " +
                        "AssignedAtUtc={AssignedAtUtc}, Partition={Partition}, Offset={Offset}",
                        assignedEvent.TicketId,
                        assignedEvent.AgentUserId,
                        assignedEvent.AssignedAtUtc,
                        result.Partition,
                        result.Offset);

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var syncService = scope.ServiceProvider
                            .GetRequiredService<TicketAssignmentSyncService>();
                        await syncService.ApplyAsync(assignedEvent, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogError(
                            exception,
                            "Failed to sync assignment for ticket {TicketId}",
                            assignedEvent.TicketId);
                    }
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(
                        exception,
                        "Kafka consume error: {Reason}",
                        exception.Error.Reason);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Received malformed TicketAssigned event");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Ticket Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }
}
