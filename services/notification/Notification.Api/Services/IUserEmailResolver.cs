namespace Notification.Api.Services;

// Resolves a user's email by ID via Auth.Api's internal endpoint. Extracted
// as an interface so TicketCreatedNotificationService can be unit-tested
// without a real HTTP call.
public interface IUserEmailResolver
{
    Task<string?> ResolveUserEmailAsync(int userId, CancellationToken cancellationToken);
}
