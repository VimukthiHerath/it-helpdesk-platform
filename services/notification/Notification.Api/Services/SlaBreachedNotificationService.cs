using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.Models;

namespace Notification.Api.Services;

// NOTIFY-4 (P2): SLA breach email to Administrator. Kept separate from the
// Kafka consumer so it's unit-testable without a broker or MySQL.
//
// NOTE: unlike TicketCreatedNotificationService, the real SlaBreachedConsumer
// already wraps its processing in a broad try/catch - preserved here so the
// PASS/FAIL contrast between the two consumers' error handling is visible in
// the test results, not just asserted in prose.
public class SlaBreachedNotificationService
{
    private const string EventType = "SlaBreached";

    private readonly NotificationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IAdminEmailResolver _adminEmailResolver;
    private readonly ILogger<SlaBreachedNotificationService> _logger;

    public SlaBreachedNotificationService(
        NotificationDbContext db,
        IEmailService emailService,
        IAdminEmailResolver adminEmailResolver,
        ILogger<SlaBreachedNotificationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _adminEmailResolver = adminEmailResolver;
        _logger = logger;
    }

    public async Task ProcessAsync(SlaBreachedEvent breachedEvent, string eventKey, CancellationToken cancellationToken)
    {
        try
        {
            var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventKey == eventKey, cancellationToken);
            if (alreadyProcessed)
            {
                _logger.LogWarning("Duplicate SlaBreached event skipped. EventKey={EventKey}", eventKey);
                return;
            }

            var adminEmails = await _adminEmailResolver.ResolveAdminEmailsAsync(cancellationToken);
            if (adminEmails.Count == 0)
            {
                _logger.LogWarning("No administrator emails found. Skipping SLA breach notification for ticket {TicketId}.", breachedEvent.TicketId);
                return;
            }

            var emailBody = BuildBreachedEmail(breachedEvent.TicketId, breachedEvent.OriginalDeadlineUtc, breachedEvent.BreachedAtUtc);

            foreach (var adminEmail in adminEmails)
            {
                await _emailService.SendAsync(
                    adminEmail,
                    $"[URGENT] SLA Breach - Ticket #{breachedEvent.TicketId}",
                    emailBody);

                _logger.LogInformation("SLA Breach alert email sent to {Recipient} for ticket {TicketId}.", adminEmail, breachedEvent.TicketId);
            }

            _db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventKey = eventKey,
                EventType = EventType,
                Recipient = string.Join(",", adminEmails),
                ProcessedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected error processing SLA Breach event");
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
}
