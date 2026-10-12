namespace Notification.Api.DTO;

// Published by Auth.Api when a user asks for a password reset. ResetLink is a
// secret (anyone holding it can take over the account until it expires), so it
// must never be logged.
public class PasswordResetRequestedEvent
{
    public string EventType { get; set; } = "PasswordResetRequested";
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string ResetLink { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RequestedAtUtc { get; set; }
}
