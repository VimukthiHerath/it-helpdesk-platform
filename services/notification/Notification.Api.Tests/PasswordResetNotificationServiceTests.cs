using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Data;
using Notification.Api.DTO;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests;

// AUTH-3: password reset email sent from a PasswordResetRequested event.
public class PasswordResetNotificationServiceTests
{
    private const string Link = "https://app.example.com/reset-password?token=abc%2Bdef%3D";

    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PasswordResetRequestedEvent CreateEvent(string email = "user@example.com", string link = Link) => new()
    {
        UserId = 7,
        Email = email,
        ResetLink = link,
        RequestedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
    };

    private static Mock<IEmailService> EmailService(bool succeeds = true)
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(succeeds);
        return mock;
    }

    private static PasswordResetNotificationService Service(NotificationDbContext db, IEmailService email) =>
        new(db, email, NullLogger<PasswordResetNotificationService>.Instance);

    [Fact]
    public async Task ProcessAsync_ValidEvent_SendsResetLinkToTheUser()
    {
        await using var db = CreateContext();
        var email = EmailService();

        await Service(db, email.Object).ProcessAsync(CreateEvent(), "PasswordResetRequested-0-1", CancellationToken.None);

        email.Verify(e => e.SendAsync(
            "user@example.com",
            It.Is<string>(s => s.Contains("Reset your password")),
            It.Is<string>(b => b.Contains("reset-password?token=abc%2Bdef%3D"))),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEventKey_DoesNotSendTwice()
    {
        await using var db = CreateContext();
        var email = EmailService();
        var service = Service(db, email.Object);

        await service.ProcessAsync(CreateEvent(), "PasswordResetRequested-0-1", CancellationToken.None);
        await service.ProcessAsync(CreateEvent(), "PasswordResetRequested-0-1", CancellationToken.None);

        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("user@example.com", "javascript:alert(1)")]
    [InlineData("user@example.com", "not a url")]
    [InlineData("user@example.com", "")]
    [InlineData("", Link)]
    public async Task ProcessAsync_InvalidEvent_SendsNothing(string address, string link)
    {
        await using var db = CreateContext();
        var email = EmailService();

        await Service(db, email.Object).ProcessAsync(CreateEvent(address, link), "PasswordResetRequested-0-2", CancellationToken.None);

        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SendFails_ThrowsAndRecordsNothingSoKafkaCanRetry()
    {
        await using var db = CreateContext();
        var email = EmailService(succeeds: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db, email.Object).ProcessAsync(CreateEvent(), "PasswordResetRequested-0-3", CancellationToken.None));

        Assert.Empty(db.ProcessedEvents);
    }

    [Fact]
    public async Task ProcessAsync_HtmlEncodesTheLinkInTheEmailBody()
    {
        await using var db = CreateContext();
        var email = EmailService();
        var link = "https://app.example.com/reset-password?token=a\"onmouseover=\"x";

        await Service(db, email.Object).ProcessAsync(CreateEvent(link: link), "PasswordResetRequested-0-4", CancellationToken.None);

        email.Verify(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<string>(b => !b.Contains("\"onmouseover=\"") && b.Contains("&quot;"))),
            Times.Once);
    }
}
