using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests;

// Fixes the gap flagged in manual-ticket-reassignment.md: nothing previously
// updated Tickets.Status/AssignedTo when Assignment.Api assigned or
// reassigned a ticket. Tested directly against the service (EF Core
// InMemory, no Kafka broker needed) rather than through the Kafka consumer -
// same split as RoundRobinAssignmentServiceTests in Assignment.Api.
public class TicketAssignmentSyncServiceTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ApplyAsync_SetsAssignedToAndStatus()
    {
        await using var context = CreateContext();
        var ticket = new Tickets
        {
            Description = "Laptop won't boot",
            IssueType = "Hardware",
            Urgency = TicketUrgency.one_hour,
            Status = TicketStatus.unassigned,
            AssignedTo = null,
            CreatedBy = 1,
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        var service = new TicketAssignmentSyncService(context, NullLogger<TicketAssignmentSyncService>.Instance);
        var assignedEvent = new TicketAssignedEvent
        {
            TicketId = ticket.Id,
            AgentUserId = 42,
            AssignedAtUtc = DateTime.UtcNow,
        };

        await service.ApplyAsync(assignedEvent, CancellationToken.None);

        var updated = await context.Tickets.FindAsync(ticket.Id);
        Assert.Equal(42, updated!.AssignedTo);
        Assert.Equal(TicketStatus.assigned, updated.Status);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task ApplyAsync_Reassignment_UpdatesAssignedToButKeepsStatusAssigned()
    {
        await using var context = CreateContext();
        var ticket = new Tickets
        {
            Description = "No internet",
            IssueType = "Network",
            Urgency = TicketUrgency.six_hours,
            Status = TicketStatus.unassigned,
            CreatedBy = 1,
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        var service = new TicketAssignmentSyncService(context, NullLogger<TicketAssignmentSyncService>.Instance);

        await service.ApplyAsync(new TicketAssignedEvent { TicketId = ticket.Id, AgentUserId = 3, AssignedAtUtc = DateTime.UtcNow }, CancellationToken.None);
        await service.ApplyAsync(new TicketAssignedEvent { TicketId = ticket.Id, AgentUserId = 26, AssignedAtUtc = DateTime.UtcNow }, CancellationToken.None);

        var updated = await context.Tickets.FindAsync(ticket.Id);
        Assert.Equal(26, updated!.AssignedTo);
        Assert.Equal(TicketStatus.assigned, updated.Status);
    }

    [Fact]
    public async Task ApplyAsync_UnknownTicket_SkipsWithoutThrowing()
    {
        await using var context = CreateContext();
        var service = new TicketAssignmentSyncService(context, NullLogger<TicketAssignmentSyncService>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            service.ApplyAsync(new TicketAssignedEvent { TicketId = 999_999, AgentUserId = 3, AssignedAtUtc = DateTime.UtcNow }, CancellationToken.None));

        Assert.Null(exception);
    }
}
