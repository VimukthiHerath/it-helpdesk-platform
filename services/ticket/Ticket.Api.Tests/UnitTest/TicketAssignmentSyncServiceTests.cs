using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class TicketAssignmentSyncServiceAdditionalTests
{
    [Fact]
    public async Task Apply_WhenTicketIsMissing_LogsExactWarningMessage()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 404, AgentUserId = 42 }, CancellationToken.None);

        VerifyLog(logger, LogLevel.Warning,
            "Received TicketAssigned for ticket 404, but no such ticket exists. Skipping.");
    }

    [Fact]
    public async Task Apply_WhenTicketExists_LogsExactInformationMessage()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Tickets.Add(new Tickets { Id = 12, Description = "Issue", IssueType = "Network" });
        await context.SaveChangesAsync();
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 12, AgentUserId = 42 }, CancellationToken.None);

        VerifyLog(logger, LogLevel.Information,
            "Synced ticket 12: now assigned to agent user 42");
    }

    [Fact]
    public async Task Apply_WhenCancellationOccurs_DoesNotPersistAndPropagatesCancellation()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Tickets.Add(new Tickets { Id = 1, Description = "Issue", IssueType = "Network" });
        await context.SaveChangesAsync();
        var cancellation = new CancellationToken(true);
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
                new TicketAssignedEvent { TicketId = 1, AgentUserId = 9 }, cancellation));
    }

    private static void VerifyLog(
        Mock<ILogger<TicketAssignmentSyncService>> logger,
        LogLevel level,
        string text) =>
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(text)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
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
        VerifyLog(logger, LogLevel.Information, Times.Once());
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
        VerifyLog(logger, LogLevel.Warning, Times.Once());
        VerifyLog(logger, LogLevel.Information, Times.Never());
    }

    private static void VerifyLog(
        Mock<ILogger<TicketAssignmentSyncService>> logger,
        LogLevel level,
        Times times) =>
        logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
