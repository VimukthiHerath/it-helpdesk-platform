using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Data;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests;

// NOTIFY-4 (P2): SLA breach email to Administrator.
// AC1: fires on SLABreached, sent to the Administrator.
// AC2: for a breached ticket, the email is sent only once.
public class SlaBreachedNotificationServiceTests
{
    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SlaBreachedEvent CreateEvent(int ticketId) => new()
    {
        TicketId = ticketId,
        OriginalDeadlineUtc = DateTime.UtcNow.AddHours(-1),
        BreachedAtUtc = DateTime.UtcNow,
    };

    private static Mock<IAdminEmailResolver> ResolverReturning(params string[] emails)
    {
        var mock = new Mock<IAdminEmailResolver>();
        mock.Setup(r => r.ResolveAdminEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emails.ToList());
        return mock;
    }

    [Fact]
    public async Task ProcessAsync_ValidEvent_SendsEmailToAllActiveAdmins()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        var resolverMock = ResolverReturning("admin1@example.com", "admin2@example.com");
        var service = new SlaBreachedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<SlaBreachedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "SlaBreached-1", CancellationToken.None);

        emailServiceMock.Verify(e => e.SendAsync("admin1@example.com", It.Is<string>(s => s.Contains("Ticket #1")), It.IsAny<string>()), Times.Once);
        emailServiceMock.Verify(e => e.SendAsync("admin2@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEvent_DoesNotResend()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        var resolverMock = ResolverReturning("admin1@example.com");
        var service = new SlaBreachedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<SlaBreachedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "SlaBreached-1", CancellationToken.None);
        await service.ProcessAsync(CreateEvent(1), "SlaBreached-1", CancellationToken.None);

        emailServiceMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoActiveAdmins_SkipsGracefullyWithoutThrowing()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        var resolverMock = ResolverReturning(); // no admins
        var service = new SlaBreachedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<SlaBreachedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "SlaBreached-1", CancellationToken.None);

        emailServiceMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Empty(context.ProcessedEvents);
    }

    [Fact]
    public async Task ProcessAsync_EmailServiceThrows_ExceptionDoesNotPropagate()
    {
        // Contrast with TicketCreatedNotificationServiceTests.
        // ProcessAsync_EmailServiceThrows_ExceptionShouldNotPropagate: unlike
        // TicketCreatedNotificationService, the real SlaBreachedConsumer
        // already wraps this in a broad try/catch, so a send failure here is
        // logged and swallowed rather than crashing the host. This test
        // passes today - it's the positive half of the BUG-04 story, proving
        // the fix pattern already exists elsewhere in this same codebase.
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new ArgumentNullException("s", "Value cannot be null."));
        var resolverMock = ResolverReturning("admin1@example.com");
        var service = new SlaBreachedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<SlaBreachedNotificationService>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            service.ProcessAsync(CreateEvent(1), "SlaBreached-1", CancellationToken.None));

        Assert.Null(exception);
    }
}
