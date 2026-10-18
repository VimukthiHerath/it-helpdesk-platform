using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Authorization;
using Ticket.Api.Controller;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class GetTicketsTests
{
    [Fact]
    public void GetTickets_ReturnsAllTickets()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.AddRange(
            new Tickets { Id = 1, Description = "Laptop issue", IssueType = "Hardware", CreatedBy = 10 },
            new Tickets { Id = 2, Description = "VPN issue", IssueType = "Network", CreatedBy = 11 });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);

        var result = Assert.IsType<OkObjectResult>(controller.GetTickets());

        var tickets = Assert.IsAssignableFrom<IEnumerable<Tickets>>(result.Value);
        Assert.Equal(new[] { 1, 2 }, tickets.Select(ticket => ticket.Id));
    }

    [Fact]
    public void GetTickets_WhenContextFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);

        var result = Assert.IsType<ObjectResult>(controller.GetTickets());

        Assert.Equal(500, result.StatusCode);

        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once
        );
    }

    [Fact]
    public void GetTickets_WhenContextFails_ReturnsExactProblemAndLogsExpectedError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);

        var result = Assert.IsType<ObjectResult>(controller.GetTickets());

        Assert.Equal("Unable to load tickets. Please try again later.",
            Assert.IsType<ProblemDetails>(result.Value).Detail);

        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString() == "Error retrieving tickets"),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
            Times.Once
        );
    }
}