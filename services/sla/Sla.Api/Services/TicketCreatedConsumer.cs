using System.Text.Json;
using Sla.Api.DTO;
using Confluent.Kafka;

namespace Sla.Api.Services;

public sealed class TicketCreatedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "sla-timers";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketCreatedConsumer> _logger;

    public TicketCreatedConsumer(
        IConfiguration configuration,
        ILogger<TicketCreatedConsumer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // Confluent.Kafka's Consume() is a blocking call, and this method never awaits,
    // so running it inline would block BackgroundService.StartAsync on the host's
    // startup thread. If Kafka isn't reachable yet, that blocks the whole host from
    // starting until it hits the startup timeout and crashes the process. Running
    // the loop on a background thread lets the host start (and Kestrel bind)
    // immediately regardless of Kafka's availability.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private void RunConsumerLoop(CancellationToken stoppingToken)
    {
        var bootstrapServers =
            _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var topic =
            _configuration["Kafka:TicketCreatedTopic"] ?? "ticket-created";

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
            "SLA timer consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
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
                            "Received an empty TicketCreated message. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    var ticketEvent =
                        JsonSerializer.Deserialize<TicketCreatedEvent>(
                            result.Message.Value,
                            new JsonSerializerOptions(
                                JsonSerializerDefaults.Web));

                    if (ticketEvent is null)
                    {
                        _logger.LogWarning(
                            "Received an invalid TicketCreated event. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    _logger.LogInformation(
                        "Received TicketCreated event. " +
                        "TicketId={TicketId}, Description={Description}, " +
                        "IssueType={IssueType}, Urgency={Urgency}, " +
                        "Status={Status}, CreatedBy={CreatedBy}, " +
                        "CreatedAtUtc={CreatedAtUtc}, Partition={Partition}, Offset={Offset}",
                        ticketEvent.TicketId,
                        ticketEvent.Description,
                        ticketEvent.IssueType,
                        ticketEvent.Urgency,
                        ticketEvent.Status,
                        ticketEvent.CreatedBy,
                        ticketEvent.CreatedAtUtc,
                        result.Partition,
                        result.Offset);
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
                        "Received malformed TicketCreated event");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "SLA timer Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }
}