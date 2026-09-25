using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.DTO;

namespace Notification.Api.Services;

public sealed class TicketCreatedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "notification-workers";
    private const string EventType = "TicketCreated";

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

    private async Task RunConsumerLoop(CancellationToken stoppingToken)
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
            "Notification consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
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

                    // AC2: Build a unique key from the Kafka partition + offset.
                    // This guarantees we never process the same message twice,
                    // even if Kafka redelivers it (at-least-once delivery).
                    var eventKey = $"{EventType}-{result.Partition.Value}-{result.Offset.Value}";

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                    var alreadyProcessed = await db.ProcessedEvents
                        .AnyAsync(e => e.EventKey == eventKey, stoppingToken);

                    if (alreadyProcessed)
                    {
                        _logger.LogWarning(
                            "Duplicate TicketCreated event skipped. EventKey={EventKey}",
                            eventKey);
                        continue;
                    }

                    // AC1: Fire real email to the requester on TicketCreated.
                    var recipientEmail = "senulmintharu2004@gmail.com"; // In production, resolve from user profile
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var emailBody = BuildTicketReceivedEmail(ticketEvent.TicketId, ticketEvent.Description, ticketEvent.IssueType, ticketEvent.CreatedAtUtc);
                    await emailService.SendAsync(
                        recipientEmail,
                        $"[IT Helpdesk] Ticket #{ticketEvent.TicketId} Received",
                        emailBody);

                    // AC3: Persist the processed event for idempotency + audit log.
                    db.ProcessedEvents.Add(new Models.ProcessedEvent
                    {
                        EventKey = eventKey,
                        EventType = EventType,
                        Recipient = recipientEmail,
                        ProcessedAtUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(stoppingToken);
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
                "Notification Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private static string BuildTicketReceivedEmail(int ticketId, string description, string issueType, DateTime createdAt)
    {
        return "<h2>We received your ticket!</h2>" +
               "<p>Hi,</p>" +
               "<p>Your IT support ticket has been successfully created.</p>" +
               "<ul>" +
               $"<li><strong>Ticket ID:</strong> #{ticketId}</li>" +
               $"<li><strong>Description:</strong> {description}</li>" +
               $"<li><strong>Issue Type:</strong> {issueType}</li>" +
               $"<li><strong>Submitted:</strong> {createdAt:f} UTC</li>" +
               "</ul>" +
               "<p>Our team will review it shortly.</p>" +
               "<p>-- IT Helpdesk Team</p>";
    }
}