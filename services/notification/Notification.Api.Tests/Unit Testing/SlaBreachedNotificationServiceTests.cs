using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Data;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class SlaBreachedNotificationServiceTests
{
    [Fact]
    public async Task ProcessAsync_NewBreach_SendsEmailToEveryAdministratorAndPersistsState()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IAdminEmailResolver>();
        resolver.Setup(x => x.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "admin1@example.com", "admin2@example.com" });
        var service = CreateService(context, email, resolver);

        await service.ProcessAsync(CreateEvent(), "SlaBreached-7", CancellationToken.None);

        email.Verify(x => x.SendAsync(
            "admin1@example.com",
            "[URGENT] SLA Breach - Ticket #7",
            It.Is<string>(body => body.Contains("#7") && body.Contains("URGENT"))), Times.Once);
        email.Verify(x => x.SendAsync(
            "admin2@example.com",
            "[URGENT] SLA Breach - Ticket #7",
            It.IsAny<string>()), Times.Once);
        var processed = await context.ProcessedEvents.SingleAsync();
        Assert.Equal("SlaBreached-7", processed.EventKey);
        Assert.Equal("SlaBreached", processed.EventType);
        Assert.Equal("admin1@example.com,admin2@example.com", processed.Recipient);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEvent_DoesNotSendOrPersistAgain()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IAdminEmailResolver>();
        resolver.Setup(x => x.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "admin@example.com" });
        var service = CreateService(context, email, resolver);

        await service.ProcessAsync(CreateEvent(), "SlaBreached-7", CancellationToken.None);
        await service.ProcessAsync(CreateEvent(), "SlaBreached-7", CancellationToken.None);

        email.Verify(
            x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task ProcessAsync_NoAdministrators_DoesNotSendOrPersist()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IAdminEmailResolver>();
        resolver.Setup(x => x.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var service = CreateService(context, email, resolver);

        await service.ProcessAsync(CreateEvent(), "SlaBreached-8", CancellationToken.None);

        email.Verify(
            x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        Assert.Empty(context.ProcessedEvents);
    }

    [Fact]
    public async Task ProcessAsync_EmailSendFails_SwallowsExceptionAndDoesNotPersist()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        var resolver = new Mock<IAdminEmailResolver>();
        resolver.Setup(x => x.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "admin@example.com" });
        var service = CreateService(context, email, resolver);

        var exception = await Record.ExceptionAsync(() =>
            service.ProcessAsync(CreateEvent(), "SlaBreached-9", CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(context.ProcessedEvents);
    }

    [Fact]
    public async Task ProcessAsync_WhenLaterAdministratorEmailFails_DoesNotPersistProcessedState()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendAsync(
                "admin2@example.com", It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        var resolver = new Mock<IAdminEmailResolver>();
        resolver.Setup(x => x.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "admin1@example.com", "admin2@example.com" });
        var service = CreateService(context, email, resolver);

        var exception = await Record.ExceptionAsync(() =>
            service.ProcessAsync(CreateEvent(), "SlaBreached-partial", CancellationToken.None));

        Assert.Null(exception);
        email.Verify(x => x.SendAsync(
            "admin1@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        email.Verify(x => x.SendAsync(
            "admin2@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Empty(context.ProcessedEvents);
    }

    private static SlaBreachedNotificationService CreateService(
        NotificationDbContext context,
        Mock<IEmailService> email,
        Mock<IAdminEmailResolver> resolver)
    {
        return new SlaBreachedNotificationService(
            context,
            email.Object,
            resolver.Object,
            new Mock<ILogger<SlaBreachedNotificationService>>().Object);
    }

    private static NotificationDbContext CreateContext()
    {
        return new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static SlaBreachedEvent CreateEvent()
    {
        return new SlaBreachedEvent
        {
            TicketId = 7,
            OriginalDeadlineUtc = new DateTime(2026, 10, 17, 7, 0, 0, DateTimeKind.Utc),
            BreachedAtUtc = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc)
        };
    }
}
