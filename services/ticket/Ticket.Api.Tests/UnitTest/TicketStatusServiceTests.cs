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

public class TicketStatusServiceBoundaryTests
{
    [Theory]
    [InlineData(TicketStatus.in_progress, true)]
    [InlineData(TicketStatus.resolved, true)]
    [InlineData(TicketStatus.closed, true)]
    [InlineData(TicketStatus.unassigned, false)]
    [InlineData(TicketStatus.assigned, false)]
    public void SettableStatuses_ContainsOnlyAgentSettableValues(
        TicketStatus status, bool expected) =>
        Assert.Equal(expected, TicketStatusService.SettableStatuses.Contains(status));
}
