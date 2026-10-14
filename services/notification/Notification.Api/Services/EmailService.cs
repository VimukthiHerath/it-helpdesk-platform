using System.Net;
using System.Net.Mail;

namespace Notification.Api.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string body)
    {
        try
        {
            var host = _configuration["Smtp:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning("SMTP Host is not configured. Skipping email dispatch to {Recipient}.", toEmail);
                return false;
            }

            var portString = _configuration["Smtp:Port"];
            if (!int.TryParse(portString, out var port))
            {
                port = 587;
            }

            var username = _configuration["Smtp:Username"] ?? string.Empty;
            var password = _configuration["Smtp:Password"] ?? string.Empty;
            var fromEmail = _configuration["Smtp:FromEmail"] ?? "noreply@helpdesk.com";
            var fromName = _configuration["Smtp:FromName"] ?? "IT Helpdesk";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(username, password)
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);

            _logger.LogInformation(
                "[EMAIL SENT] To={Recipient}, Subject={Subject}, SentAtUtc={SentAt}",
                toEmail, subject, DateTime.UtcNow);
                
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient} due to SMTP/Network error. System will continue operating.", toEmail);
            return false;
        }
    }
}
