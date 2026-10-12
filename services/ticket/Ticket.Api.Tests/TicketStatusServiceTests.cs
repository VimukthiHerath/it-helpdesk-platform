using Microsoft.EntityFrameworkCore;
using Ticket.Api.Data;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests;

// SCRUM-17. EF Core InMemory, no HTTP involved - proves AC3 (a full
// history is retained, not just the current value) directly against the
// service, same pattern as TicketAssignmentSyncServiceTests.
public class TicketStatusServiceTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ApplyStatusChangeAsync_UpdatesTicketStatusAndUpdatedAt()
    {
        await using var context = CreateContext();
        var ticket = new Tickets
        {
            Description = "Printer jam",
            IssueType = "Hardware",
            Urgency = TicketUrgency.one_hour,
            Status = TicketStatus.assigned,
            AssignedTo = 3,
            CreatedBy = 1,
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        var service = new TicketStatusService(context);
        await service.ApplyStatusChangeAsync(ticket, TicketStatus.in_progress, changedBy: 3, CancellationToken.None);

        var updated = await context.Tickets.FindAsync(ticket.Id);
        Assert.Equal(TicketStatus.in_progress, updated!.Status);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_RecordsAHistoryRowWithOldAndNewStatus()
    {
        await using var context = CreateContext();
        var ticket = new Tickets
        {
            Description = "VPN not connecting",
            IssueType = "Network",
            Urgency = TicketUrgency.six_hours,
            Status = TicketStatus.assigned,
            AssignedTo = 3,
            CreatedBy = 1,
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        var service = new TicketStatusService(context);
        await service.ApplyStatusChangeAsync(ticket, TicketStatus.in_progress, changedBy: 3, CancellationToken.None);

        var entry = Assert.Single(context.StatusHistory);
        Assert.Equal(ticket.Id, entry.TicketId);
        Assert.Equal(TicketStatus.assigned, entry.OldStatus);
        Assert.Equal(TicketStatus.in_progress, entry.NewStatus);
        Assert.Equal(3, entry.ChangedBy);
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_MultipleChanges_KeepsEveryHistoryRow()
    {
        // AC3: "a full history, not just the current value" - proves
        // earlier rows survive later changes rather than being overwritten.
        await using var context = CreateContext();
        var ticket = new Tickets
        {
            Description = "Monitor flickering",
            IssueType = "Hardware",
            Urgency = TicketUrgency.twelve_hours,
            Status = TicketStatus.assigned,
            AssignedTo = 3,
            CreatedBy = 1,
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();

        var service = new TicketStatusService(context);
        await service.ApplyStatusChangeAsync(ticket, TicketStatus.in_progress, changedBy: 3, CancellationToken.None);
        await service.ApplyStatusChangeAsync(ticket, TicketStatus.resolved, changedBy: 3, CancellationToken.None);
        await service.ApplyStatusChangeAsync(ticket, TicketStatus.closed, changedBy: 26, CancellationToken.None);

        var history = await context.StatusHistory
            .Where(h => h.TicketId == ticket.Id)
            .OrderBy(h => h.Id)
            .ToListAsync();

        Assert.Equal(3, history.Count);
        Assert.Equal((TicketStatus.assigned, TicketStatus.in_progress), (history[0].OldStatus, history[0].NewStatus));
        Assert.Equal((TicketStatus.in_progress, TicketStatus.resolved), (history[1].OldStatus, history[1].NewStatus));
        Assert.Equal((TicketStatus.resolved, TicketStatus.closed), (history[2].OldStatus, history[2].NewStatus));
        Assert.Equal(TicketStatus.closed, (await context.Tickets.FindAsync(ticket.Id))!.Status);
    }
}
