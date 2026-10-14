using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class GetStatusHistoryTests
{
    [Fact]
    public async Task GetStatusHistory_ReturnsHistoryInChronologicalOrder()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Issue", IssueType = "Software", CreatedBy = 1, AssignedTo = 42
        });
        context.StatusHistory.AddRange(
            new TicketStatusHistory
            {
                TicketId = 7, OldStatus = TicketStatus.in_progress, NewStatus = TicketStatus.resolved,
                ChangedBy = 42, ChangedAtUtc = DateTime.UtcNow.AddMinutes(1)
            },
            new TicketStatusHistory
            {
                TicketId = 7, OldStatus = TicketStatus.assigned, NewStatus = TicketStatus.in_progress,
                ChangedBy = 42, ChangedAtUtc = DateTime.UtcNow
            });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = Assert.IsType<OkObjectResult>(await controller.GetStatusHistory(7));

        var history = Assert.IsAssignableFrom<IEnumerable<TicketStatusHistoryItemDTO>>(result.Value);
        Assert.Equal(new[] { TicketStatus.in_progress, TicketStatus.resolved },
            history.Select(item => item.NewStatus));
    }

    [Fact]
    public async Task GetStatusHistory_WhenTicketDoesNotExist_ReturnsNotFound()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.GetStatusHistory(7);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetStatusHistory_WhenAgentIsNotAssigned_ReturnsForbidden()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Issue", IssueType = "Software", CreatedBy = 1, AssignedTo = 99
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.GetStatusHistory(7);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
