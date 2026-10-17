using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class EmailServiceTests
{
    [Fact]
    public async Task SendAsync_InvalidPort_ThrowsFormatExceptionBeforeSending()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "not-a-port"
        });

        await Assert.ThrowsAsync<FormatException>(() =>
            service.SendAsync("recipient@example.com", "Subject", "<p>Body</p>"));
    }

    [Fact]
    public async Task SendAsync_MissingPort_ThrowsBeforeCreatingSmtpConnection()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com"
        });

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SendAsync("recipient@example.com", "Subject", "<p>Body</p>"));
    }

    [Fact]
    public async Task SendAsync_InvalidFromAddress_ThrowsBeforeNetworkSend()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "25",
            ["Smtp:Username"] = "username",
            ["Smtp:Password"] = "password",
            ["Smtp:FromEmail"] = "not-an-email",
            ["Smtp:FromName"] = "IT Helpdesk"
        });

        await Assert.ThrowsAsync<FormatException>(() =>
            service.SendAsync("recipient@example.com", "Subject", "<p>Body</p>"));
    }

    private static EmailService CreateService(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new EmailService(
            configuration,
            new Mock<ILogger<EmailService>>().Object);
    }
}
