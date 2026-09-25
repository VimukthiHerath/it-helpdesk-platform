using System.Text.Json;
using Sla.Api.DTO;
using Confluent.Kafka;
using Sla.Api.Data;
using Sla.Api.Models;

namespace Sla.Api.Services;

public sealed class TicketCreatedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "sla-timers";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketCreatedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketCreatedConsumer(
        IConfiguration configuration,
        ILogger<TicketCreatedConsumer> logger,
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
                        "Received TicketCreated event. TicketId={TicketId}, Urgency={Urgency}, Partition={Partition}, Offset={Offset}",
                        ticketEvent.TicketId,
                        ticketEvent.Urgency,
                        result.Partition,
                        result.Offset);

                    // Map urgency to deadline duration based on frontend labels
                    // (0 = 1 hour, 1 = 6 hours, 2 = 12 hours, 3 = 24 hours)
                    TimeSpan slaDuration = ticketEvent.Urgency switch
                    {
                        0 => TimeSpan.FromHours(1),
                        1 => TimeSpan.FromHours(6),
                        2 => TimeSpan.FromHours(12),
                        3 => TimeSpan.FromHours(24),
                        _ => TimeSpan.FromHours(24) // Default fallback
                    };

                    DateTime deadlineUtc = ticketEvent.CreatedAtUtc.Add(slaDuration);

                    string eventKey = $"TicketCreated-{result.Partition}-{result.Offset}";

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<SlaDbContext>();

                        // Idempotency check
                        bool alreadyProcessed = db.ProcessedEvents.Any(e => e.EventKey == eventKey);
                        if (alreadyProcessed)
                        {
                            _logger.LogInformation("Event {EventKey} already processed. Skipping.", eventKey);
                            continue;
                        }

                        // Create SLA record
                        var ticketSla = new TicketSla
                        {
                            TicketId = ticketEvent.TicketId,
                            Urgency = ticketEvent.Urgency,
                            CreatedAtUtc = ticketEvent.CreatedAtUtc,
                            DeadlineUtc = deadlineUtc,
                            Status = "Active"
                        };

                        db.TicketSlas.Add(ticketSla);

                        // Record idempotency
                        db.ProcessedEvents.Add(new ProcessedEvent
                        {
                            EventKey = eventKey,
                            ProcessedAtUtc = DateTime.UtcNow
                        });

                        db.SaveChanges();
                    }

                    _logger.LogInformation(
                        "Successfully calculated and saved SLA deadline for Ticket {TicketId}. DeadlineUtc={DeadlineUtc}",
                        ticketEvent.TicketId,
                        deadlineUtc);
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
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "An error occurred while processing TicketCreated event");
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