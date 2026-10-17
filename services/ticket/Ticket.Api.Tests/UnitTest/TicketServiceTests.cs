using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class TicketStatusServiceTests
{
    [Fact]
    public async Task ApplyStatusChange_UpdatesTicketAndAddsCompleteHistory()
    {
        var (context, _, _, _, _) = TicketControllerTestFixture.CreateDependencies();
        var ticket = new Tickets
        {
            Id = 12,
            Description = "Issue",
            IssueType = "Software",
            CreatedBy = 7,
            Status = TicketStatus.assigned,
            UpdatedAt = null
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();
        var before = DateTime.UtcNow;

        var result = await new TicketStatusService(context).ApplyStatusChangeAsync(
            ticket, TicketStatus.closed, 42, CancellationToken.None);

        Assert.Same(ticket, result);
        Assert.Equal(TicketStatus.closed, ticket.Status);
        Assert.NotNull(ticket.UpdatedAt);
        Assert.InRange(ticket.UpdatedAt.Value, before, DateTime.UtcNow);
        context.ChangeTracker.Clear();
        var persistedTicket = await context.Tickets.FindAsync(12);
        var history = Assert.Single(context.StatusHistory);
        Assert.NotNull(persistedTicket);
        Assert.Equal(TicketStatus.closed, persistedTicket.Status);
        Assert.Equal(12, history.TicketId);
        Assert.Equal(TicketStatus.assigned, history.OldStatus);
        Assert.Equal(TicketStatus.closed, history.NewStatus);
        Assert.Equal(42, history.ChangedBy);
    }
}

public class TicketAssignmentSyncServiceTests
{
    [Fact]
    public async Task Apply_WhenTicketExists_SyncsAssignmentAndStatus()
    {
        var (context, _, _, _, _) = TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 12,
            Description = "Issue",
            IssueType = "Software",
            CreatedBy = 7,
            Status = TicketStatus.unassigned
        });
        await context.SaveChangesAsync();
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();
        var before = DateTime.UtcNow;

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 12, AgentUserId = 42 },
            CancellationToken.None);

        var ticket = Assert.Single(context.Tickets);
        Assert.Equal(42, ticket.AssignedTo);
        Assert.Equal(TicketStatus.assigned, ticket.Status);
        Assert.NotNull(ticket.UpdatedAt);
        Assert.InRange(ticket.UpdatedAt.Value, before, DateTime.UtcNow);
        context.ChangeTracker.Clear();
        var persistedTicket = await context.Tickets.FindAsync(12);
        Assert.NotNull(persistedTicket);
        Assert.Equal(42, persistedTicket.AssignedTo);
        Assert.Equal(TicketStatus.assigned, persistedTicket.Status);
        logger.VerifyLog(LogLevel.Information, Times.Once());
    }

    [Fact]
    public async Task Apply_WhenTicketDoesNotExist_SkipsPersistenceAndLogsWarning()
    {
        var (context, _, _, _, _) = TicketControllerTestFixture.CreateDependencies();
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 404, AgentUserId = 42 },
            CancellationToken.None);

        Assert.Empty(context.Tickets);
        logger.VerifyLog(LogLevel.Warning, Times.Once());
        logger.VerifyLog(LogLevel.Information, Times.Never());
    }
}

internal static class LoggerMoqExtensions
{
    internal static void VerifyLog<T>(
        this Mock<ILogger<T>> logger,
        LogLevel level,
        Times times)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
