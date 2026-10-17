using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.DTO;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class NotificationBoundaryTests
{
    [Fact]
    public async Task EmailService_MissingHost_ThrowsBeforeNetworkSend()
    {
        var service = CreateEmailService(new Dictionary<string, string?>
        {
            ["Smtp:Port"] = "25",
            ["Smtp:Username"] = "user",
            ["Smtp:Password"] = "password",
            ["Smtp:FromEmail"] = "from@example.com",
            ["Smtp:FromName"] = "Helpdesk"
        });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SendAsync("to@example.com", "Subject", "Body"));
    }

    [Fact]
    public async Task EmailService_InvalidRecipient_ThrowsBeforeNetworkSend()
    {
        var service = CreateEmailService(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "25",
            ["Smtp:Username"] = "user",
            ["Smtp:Password"] = "password",
            ["Smtp:FromEmail"] = "from@example.com",
            ["Smtp:FromName"] = "Helpdesk"
        });

        await Assert.ThrowsAsync<FormatException>(() =>
            service.SendAsync("not-an-email", "Subject", "Body"));
    }

    [Fact]
    public void TicketCreatedEvent_DefaultsEventTypeToTicketCreated()
    {
        Assert.Equal("TicketCreated", new TicketCreatedEvent().EventType);
    }

    private static EmailService CreateEmailService(Dictionary<string, string?> settings) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new Mock<ILogger<EmailService>>().Object);
}
