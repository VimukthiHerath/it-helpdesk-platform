using Microsoft.AspNetCore.Mvc;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class GetMyTicketsTests
{
    [Fact]
    public void GetMyTickets_ReturnsOnlyTicketsCreatedByCaller()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.AddRange(
            new Tickets { Id = 1, Description = "Mine", IssueType = "Software", CreatedBy = 42 },
            new Tickets { Id = 2, Description = "Other", IssueType = "Hardware", CreatedBy = 99 });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = Assert.IsType<OkObjectResult>(controller.GetMyTickets());

        var tickets = Assert.IsAssignableFrom<IEnumerable<Tickets>>(result.Value);
        Assert.Single(tickets);
        Assert.Equal(1, tickets.Single().Id);
    }

    [Fact]
    public void GetMyTickets_WhenContextFails_ReturnsProblem()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = Assert.IsType<ObjectResult>(controller.GetMyTickets());

        Assert.Equal(500, result.StatusCode);
    }
}
