namespace Notification.Api.Services;

public interface IEmailService
{
    Task<bool> SendAsync(string toEmail, string subject, string body);
}
