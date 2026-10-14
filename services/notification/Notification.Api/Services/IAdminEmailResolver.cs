namespace Notification.Api.Services;

// Resolves all active administrators' emails via Auth.Api's internal
// endpoint. Extracted as an interface so SlaBreachedNotificationService can
// be unit-tested without a real HTTP call.
public interface IAdminEmailResolver
{
    Task<List<string>> ResolveAdminEmailsAsync(CancellationToken cancellationToken);
}
