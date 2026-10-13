using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Data;
using Notification.Api.DTO;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests;

// NOTIFY-2 (P2): ticket-received email.
// AC1: fires on TicketCreated, sent to the requester.
// AC2: idempotency - the exact event is not processed twice.
// AC3: every send is logged (event type, recipient, timestamp).
public class TicketCreatedNotificationServiceTests
{
    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TicketCreatedEvent CreateEvent(int ticketId, int createdBy = 5) => new()
    {
        TicketId = ticketId,
        Description = "Printer won't turn on",
        IssueType = "Hardware",
        Urgency = 0,
        CreatedBy = createdBy,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static Mock<IUserEmailResolver> ResolverReturning(string email)
    {
        var mock = new Mock<IUserEmailResolver>();
        mock.Setup(r => r.ResolveUserEmailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(email);
        return mock;
    }

    [Fact]
    public async Task ProcessAsync_ValidEvent_SendsEmailToRequester()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var resolverMock = ResolverReturning("employee@example.com");
        var service = new TicketCreatedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<TicketCreatedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "TicketCreated-0-1", CancellationToken.None);

        emailServiceMock.Verify(e => e.SendAsync(
            "employee@example.com",
            It.Is<string>(s => s.Contains("Ticket #1")),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEventKey_DoesNotSendTwice()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var resolverMock = ResolverReturning("employee@example.com");
        var service = new TicketCreatedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<TicketCreatedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "TicketCreated-0-1", CancellationToken.None);
        await service.ProcessAsync(CreateEvent(1), "TicketCreated-0-1", CancellationToken.None); // same eventKey

        emailServiceMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_EverySend_IsRecordedWithRecipientAndTimestamp()
    {
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var resolverMock = ResolverReturning("employee@example.com");
        var service = new TicketCreatedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<TicketCreatedNotificationService>.Instance);

        await service.ProcessAsync(CreateEvent(1), "TicketCreated-0-1", CancellationToken.None);

        var record = Assert.Single(context.ProcessedEvents);
        Assert.Equal("employee@example.com", record.Recipient);
        Assert.True(record.ProcessedAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }



    [Fact]
    public async Task ProcessAsync_EmailServiceThrows_NoIdempotencyRecordIsWritten()
    {
        // BUG-05 regression test: this documents (not just asserts) the
        // hazardous ordering - the ProcessedEvents row is only written
        // *after* a successful send. Combined with Kafka's EnableAutoCommit=true
        // (which commits the offset independently of processing success),
        // a crash here means the message is both unsent AND untracked - if
        // the offset already advanced, it is never retried and the
        // notification is lost forever with no trace. This test passes,
        // which is itself the point: it proves the gap exists.
        await using var context = CreateContext();
        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new ArgumentNullException("s", "Value cannot be null."));
        var resolverMock = ResolverReturning("employee@example.com");
        var service = new TicketCreatedNotificationService(context, emailServiceMock.Object, resolverMock.Object, NullLogger<TicketCreatedNotificationService>.Instance);

        try
        {
            await service.ProcessAsync(CreateEvent(1), "TicketCreated-0-1", CancellationToken.None);
        }
        catch (ArgumentNullException)
        {
            // expected given today's code - see BUG-04
        }

        Assert.Empty(context.ProcessedEvents);
    }
}
