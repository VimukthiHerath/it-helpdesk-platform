using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Data;
using Notification.Api.DTO;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class TicketCreatedNotificationServiceTests
{
    [Fact]
    public async Task ProcessAsync_NewEvent_SendsEmailAndPersistsProcessedEvent()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IUserEmailResolver>();
        resolver.Setup(x => x.ResolveUserEmailAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("user@example.com");
        var service = CreateService(context, email, resolver);
        var ticketEvent = CreateEvent();

        await service.ProcessAsync(ticketEvent, "TicketCreated-0-10", CancellationToken.None);

        email.Verify(x => x.SendAsync(
            "user@example.com",
            "[IT Helpdesk] Ticket #7 Received",
            It.Is<string>(body =>
                body.Contains("#7") &&
                body.Contains("Printer") &&
                body.Contains("Hardware"))), Times.Once);
        var processed = await context.ProcessedEvents.SingleAsync();
        Assert.Equal("TicketCreated-0-10", processed.EventKey);
        Assert.Equal("TicketCreated", processed.EventType);
        Assert.Equal("user@example.com", processed.Recipient);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEvent_DoesNotResolveOrSendOrPersist()
    {
        await using var context = CreateContext();
        context.ProcessedEvents.Add(new Models.ProcessedEvent
        {
            EventKey = "TicketCreated-0-10",
            EventType = "TicketCreated",
            Recipient = "user@example.com"
        });
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IUserEmailResolver>();
        var service = CreateService(context, email, resolver);

        await service.ProcessAsync(CreateEvent(), "TicketCreated-0-10", CancellationToken.None);

        resolver.Verify(
            x => x.ResolveUserEmailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        email.Verify(
            x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task ProcessAsync_UserEmailCannotBeResolved_DoesNotSendOrPersist()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        var resolver = new Mock<IUserEmailResolver>();
        resolver.Setup(x => x.ResolveUserEmailAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var service = CreateService(context, email, resolver);

        await service.ProcessAsync(CreateEvent(), "TicketCreated-0-11", CancellationToken.None);

        email.Verify(
            x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        Assert.Empty(context.ProcessedEvents);
    }

    [Fact]
    public async Task ProcessAsync_EmailSendFails_DoesNotPersistAndPropagatesException()
    {
        await using var context = CreateContext();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));
        var resolver = new Mock<IUserEmailResolver>();
        resolver.Setup(x => x.ResolveUserEmailAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("user@example.com");
        var service = CreateService(context, email, resolver);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessAsync(CreateEvent(), "TicketCreated-0-12", CancellationToken.None));

        Assert.Empty(context.ProcessedEvents);
    }

    private static TicketCreatedNotificationService CreateService(
        NotificationDbContext context,
        Mock<IEmailService> email,
        Mock<IUserEmailResolver> resolver)
    {
        return new TicketCreatedNotificationService(
            context,
            email.Object,
            resolver.Object,
            new Mock<ILogger<TicketCreatedNotificationService>>().Object);
    }

    private static NotificationDbContext CreateContext()
    {
        return new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static TicketCreatedEvent CreateEvent()
    {
        return new TicketCreatedEvent
        {
            TicketId = 7,
            CreatedBy = 42,
            Description = "Printer is not working",
            IssueType = "Hardware",
            CreatedAtUtc = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc)
        };
    }
}
