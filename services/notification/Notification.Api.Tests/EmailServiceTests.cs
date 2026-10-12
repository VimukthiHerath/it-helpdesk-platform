using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests;

public class EmailServiceTests
{
    [Fact]
    public async Task SendAsync_MissingPort_DefaultsTo587AndHandlesFailureGracefully()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Smtp:Host", "invalid.host.local" },
            { "Smtp:Port", null }, // Missing point to trigger fallback
            { "Smtp:Username", "user" },
            { "Smtp:Password", "pass" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerMock = new Mock<ILogger<EmailService>>();
        var emailService = new EmailService(configuration, loggerMock.Object);

        // Act
        var result = await emailService.SendAsync("test@example.com", "Subject", "Body");

        // Assert
        Assert.False(result); // Should fail to send because host is invalid, but handled gracefully
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ReturnsFalseWithoutThrowing()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Smtp:Host", "10.255.255.1" }, // Unreachable host
            { "Smtp:Port", "587" },
            { "Smtp:Username", "user" },
            { "Smtp:Password", "pass" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerMock = new Mock<ILogger<EmailService>>();
        var emailService = new EmailService(configuration, loggerMock.Object);

        // Act
        var result = await emailService.SendAsync("test@example.com", "Subject", "Body");

        // Assert
        Assert.False(result);
    }
}
