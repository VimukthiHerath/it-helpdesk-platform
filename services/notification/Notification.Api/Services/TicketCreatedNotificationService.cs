using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.DTO;
using Notification.Api.Models;

namespace Notification.Api.Services;

// NOTIFY-2 (P2): ticket-received email. Kept separate from the Kafka
// consumer so it's unit-testable without a broker or MySQL - same pattern as
// Assignment.Api's RoundRobinAssignmentService.
//
// NOTE: this faithfully preserves the consumer's current behaviour,
// including its known gap - there is deliberately no broad try/catch around
// the email send here, matching TicketCreatedConsumer.cs today. See
// docs/TEMP_BUGS_SPRINT3/BUG-04 and BUG-05: an EmailService failure
// propagates uncaught, and because the ProcessedEvents row is only written
// *after* a successful send, a failure here also means the idempotency
// record for this message is never written.
public class TicketCreatedNotificationService
{
    private const string EventType = "TicketCreated";

    private readonly NotificationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IUserEmailResolver _userEmailResolver;
    private readonly ILogger<TicketCreatedNotificationService> _logger;

    public TicketCreatedNotificationService(
        NotificationDbContext db,
        IEmailService emailService,
        IUserEmailResolver userEmailResolver,
        ILogger<TicketCreatedNotificationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _userEmailResolver = userEmailResolver;
        _logger = logger;
    }

    public async Task ProcessAsync(TicketCreatedEvent ticketEvent, string eventKey, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventKey == eventKey, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogWarning("Duplicate TicketCreated event skipped. EventKey={EventKey}", eventKey);
            return;
        }

        var recipientEmail = await _userEmailResolver.ResolveUserEmailAsync(ticketEvent.CreatedBy, cancellationToken);
        if (recipientEmail is null)
        {
            _logger.LogWarning(
                "Could not resolve email for user {UserId}. Skipping notification for ticket {TicketId}.",
                ticketEvent.CreatedBy, ticketEvent.TicketId);
            return;
        }

        var emailBody = BuildTicketReceivedEmail(ticketEvent.TicketId, ticketEvent.Description, ticketEvent.IssueType, ticketEvent.CreatedAtUtc);

        // No try/catch here - see the class-level note above.
        await _emailService.SendAsync(
            recipientEmail,
            $"[IT Helpdesk] Ticket #{ticketEvent.TicketId} Received",
            emailBody);

        _logger.LogInformation("Ticket received email sent to {Recipient} for ticket {TicketId}.", recipientEmail, ticketEvent.TicketId);

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventKey = eventKey,
            EventType = EventType,
            Recipient = recipientEmail,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
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
