using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
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

    [Fact]
    public async Task GetStatusHistory_WhenContextFails_ReturnsExactProblemAndLogsExpectedError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.GetStatusHistory(7);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal("Unable to load status history. Please try again later.",
            Assert.IsType<ProblemDetails>(problem.Value).Detail);
        VerifyLog(logger, LogLevel.Error, "Error retrieving status history for ticket {TicketId}");
    }

    [Fact]
    public async Task GetStatusHistory_WhenTicketIsMissing_ReturnsExactMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = Assert.IsType<NotFoundObjectResult>(await controller.GetStatusHistory(7));

        Assert.Equal("Ticket not found.", GetProperty(result.Value!, "message"));
    }

    [Fact]
    public async Task GetStatusHistory_WhenAgentIsNotAssigned_ReturnsExactMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets { Id = 7, AssignedTo = 99 });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = Assert.IsType<ObjectResult>(await controller.GetStatusHistory(7));

        Assert.Equal("You can only act on tickets assigned to you.",
            GetProperty(result.Value!, "message"));
    }

    private static object? GetProperty(object value, string name) =>
        value.GetType().GetProperty(name)!.GetValue(value);

    private static void VerifyLog(Mock<ILogger<TicketController>> logger, LogLevel level, string message)
    {
        var prefix = message.Split('{')[0].TrimEnd();
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.StartsWith(prefix)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
