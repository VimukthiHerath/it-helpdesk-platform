using System.Net;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.DTO;
using Notification.Api.Models;

namespace Notification.Api.Services;

// AUTH-3: password reset email. Kept separate from the Kafka consumer so it is
// unit-testable without a broker or MySQL, like the other notification services.
//
// The reset link is a secret, so it is never written to the logs.
public class PasswordResetNotificationService
{
    private const string EventType = "PasswordResetRequested";

    private readonly NotificationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<PasswordResetNotificationService> _logger;

    public PasswordResetNotificationService(
        NotificationDbContext db,
        IEmailService emailService,
        ILogger<PasswordResetNotificationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ProcessAsync(PasswordResetRequestedEvent resetEvent, string eventKey, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventKey == eventKey, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogWarning("Duplicate PasswordResetRequested event skipped. EventKey={EventKey}", eventKey);
            return;
        }

        if (string.IsNullOrWhiteSpace(resetEvent.Email) || !IsHttpUrl(resetEvent.ResetLink))
        {
            _logger.LogWarning(
                "Invalid PasswordResetRequested event for user {UserId}: missing email or a reset link that is not http(s). Skipping.",
                resetEvent.UserId);
            return;
        }

        var sent = await _emailService.SendAsync(
            resetEvent.Email,
            "[IT Helpdesk] Reset your password",
            BuildEmail(resetEvent.ResetLink, resetEvent.ExpiresAtUtc));

        if (!sent)
        {
            throw new InvalidOperationException("Failed to send password reset email. Aborting to allow Kafka retry.");
        }

        _logger.LogInformation("Password reset email sent for user {UserId}.", resetEvent.UserId);

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventKey = eventKey,
            EventType = EventType,
            Recipient = resetEvent.Email,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string BuildEmail(string resetLink, DateTime expiresAtUtc)
    {
        var link = WebUtility.HtmlEncode(resetLink);
        return "<h2>Reset your password</h2>" +
               "<p>Hi,</p>" +
               "<p>We received a request to reset the password for your IT Helpdesk account.</p>" +
               $"<p><a href=\"{link}\">Choose a new password</a></p>" +
               $"<p>This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC and can be used once.</p>" +
               "<p>If you did not ask for this, you can ignore this email. Your password will not change.</p>" +
               "<p>- IT Helpdesk</p>";
    }
}
