using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.DTO;

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
                    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                    var alreadyProcessed = await db.ProcessedEvents
                        .AnyAsync(e => e.EventKey == eventKey, stoppingToken);

                    if (alreadyProcessed)
                    {
                        _logger.LogWarning("Duplicate SlaBreached event skipped. EventKey={EventKey}", eventKey);
                        continue;
                    }

                    // Dynamically fetch all administrators' emails from Auth.Api
                    var adminEmails = await ResolveAdminEmailsAsync(scope);
                    if (adminEmails.Count == 0)
                    {
                        _logger.LogWarning("No administrator emails found. Skipping SLA breach notification for ticket {TicketId}.", breachedEvent.TicketId);
                        continue;
                    }

                    // AC1: Send email to all administrators
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var emailBody = BuildBreachedEmail(breachedEvent.TicketId, breachedEvent.OriginalDeadlineUtc, breachedEvent.BreachedAtUtc);
                    
                    foreach (var adminEmail in adminEmails)
                    {
                        await emailService.SendAsync(
                            adminEmail,
                            $"[URGENT] SLA Breach - Ticket #{breachedEvent.TicketId}",
                            emailBody);

                        _logger.LogInformation("SLA Breach alert email sent to {Recipient} for ticket {TicketId}.", adminEmail, breachedEvent.TicketId);
                    }

                    // Persist processed event. Since we send to multiple admins, we log the first one or just 'Admins'.
                    db.ProcessedEvents.Add(new Models.ProcessedEvent
                    {
                        EventKey = eventKey,
                        EventType = EventType,
                        Recipient = string.Join(",", adminEmails),
                        ProcessedAtUtc = DateTime.UtcNow
                    });
                    
                    await db.SaveChangesAsync(stoppingToken);
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

    private async Task<List<string>> ResolveAdminEmailsAsync(IServiceScope scope)
    {
        var authApiUrl = _configuration["AuthApiUrl"] ?? "http://localhost:5121";
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient();

        try
        {
            var response = await client.GetFromJsonAsync<AdminEmailsDto>(
                $"{authApiUrl}/api/auth/internal/admins/emails");
            return response?.Emails ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve administrator emails from Auth.Api.");
            return new List<string>();
        }
    }

    private static string BuildBreachedEmail(int ticketId, DateTime originalDeadline, DateTime breachedAt)
    {
        return "<h2>URGENT: Ticket SLA Breach</h2>" +
               "<p>Administrator,</p>" +
               $"<p>Ticket <strong>#{ticketId}</strong> has breached its SLA and requires immediate intervention.</p>" +
               "<ul>" +
               $"<li><strong>Original Deadline:</strong> {originalDeadline:f} UTC</li>" +
               $"<li><strong>Breach Detected At:</strong> {breachedAt:f} UTC</li>" +
               "</ul>" +
               "<p>Please review and re-assign this ticket immediately.</p>" +
               "<p>-- IT Helpdesk System</p>";
    }

    private sealed record AdminEmailsDto(List<string> Emails);
}

public class SlaBreachedEvent
{
    public int TicketId { get; set; }
    public DateTime OriginalDeadlineUtc { get; set; }
    public DateTime BreachedAtUtc { get; set; }
}
