using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.DTO;

namespace Notification.Api.Services;

public sealed class TicketAssignedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "notification-assignment-workers";
    private const string EventType = "TicketAssigned";

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

    // Confluent.Kafka's Consume() is a blocking call — run on a background
    // thread so it never blocks the host startup thread.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private async Task RunConsumerLoop(CancellationToken stoppingToken)
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
            "Assignment-notification consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
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

                    // AC2: Build a unique key from the Kafka partition + offset.
                    // Same idempotency pattern as NOTIFY-2/TicketCreatedConsumer —
                    // guarantees we never send the same assignment email twice even
                    // if Kafka redelivers the message.
                    var eventKey = $"{EventType}-{result.Partition.Value}-{result.Offset.Value}";

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                    var alreadyProcessed = await db.ProcessedEvents
                        .AnyAsync(e => e.EventKey == eventKey, stoppingToken);

                    if (alreadyProcessed)
                    {
                        _logger.LogWarning(
                            "Duplicate TicketAssigned event skipped. EventKey={EventKey}",
                            eventKey);
                        continue;
                    }

                    // AC1: Fire email to the assigned agent on TicketAssigned.
                    // In development we send to the test inbox so it can be verified
                    // manually; in production this would resolve the agent's email
                    // from the Auth service using their AgentUserId.
                    var recipientEmail = "senulmintharu2004@gmail.com"; // TODO: resolve from Auth.Api in production
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var emailBody = BuildTicketAssignedEmail(assignedEvent.TicketId, assignedEvent.AgentUserId, assignedEvent.AssignedAtUtc);
                    await emailService.SendAsync(
                        recipientEmail,
                        $"[IT Helpdesk] Ticket #{assignedEvent.TicketId} Assigned to You",
                        emailBody);

                    _logger.LogInformation(
                        "Assignment email sent. TicketId={TicketId}, AgentUserId={AgentUserId}",
                        assignedEvent.TicketId,
                        assignedEvent.AgentUserId);

                    // Persist the processed event for idempotency + audit log.
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
                        "Received malformed TicketAssigned event");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Assignment-notification Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private static string BuildTicketAssignedEmail(int ticketId, int agentUserId, DateTime assignedAt)
    {
        return "<h2>A ticket has been assigned to you!</h2>" +
               "<p>Hi,</p>" +
               "<p>A new IT support ticket has been assigned to you. Please review and action it as soon as possible.</p>" +
               "<ul>" +
               $"<li><strong>Ticket ID:</strong> #{ticketId}</li>" +
               $"<li><strong>Assigned Agent ID:</strong> {agentUserId}</li>" +
               $"<li><strong>Assigned At:</strong> {assignedAt:f} UTC</li>" +
               "</ul>" +
               "<p>Please log in to the IT Helpdesk portal to view the full ticket details and respond to the requester.</p>" +
               "<p>-- IT Helpdesk System</p>";
    }
}
