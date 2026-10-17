using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
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

    [Fact]
    public void GetMyTickets_UsesJwtSubjectBeforeNameIdentifier()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.AddRange(
            new Tickets { Id = 1, Description = "Subject", CreatedBy = 7 },
            new Tickets { Id = 2, Description = "Name identifier", CreatedBy = 8 });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 8);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "8"),
            new Claim(JwtRegisteredClaimNames.Sub, "7")
        }, "Test"));

        var result = Assert.IsType<OkObjectResult>(controller.GetMyTickets());

        Assert.Equal(1, Assert.Single(
            Assert.IsAssignableFrom<IEnumerable<Tickets>>(result.Value)).Id);
    }

    [Fact]
    public void GetMyTickets_WhenContextFails_ReturnsExactProblemAndLogsExpectedError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = Assert.IsType<ObjectResult>(controller.GetMyTickets());

        Assert.Equal("Unable to retrieve my tickets. Please try again later.",
            Assert.IsType<ProblemDetails>(result.Value).Detail);
        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.StartsWith("Error retrieving my tickets")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
