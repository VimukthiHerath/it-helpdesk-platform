using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class GetQueueTests
{
    [Fact]
    public void GetQueue_ReturnsOnlyCallerAssignmentsInUrgencyAndFifoOrder()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.AddRange(new Agent { Id = 1, UserId = 7 }, new Agent { Id = 2, UserId = 8 });
        var oldest = DateTime.UtcNow.AddMinutes(-10);
        context.Assignments.AddRange(
            new TicketAssignment { TicketId = 20, AgentId = 1, Urgency = 1, AssignedAtUtc = oldest },
            new TicketAssignment { TicketId = 21, AgentId = 1, Urgency = 0, AssignedAtUtc = oldest.AddMinutes(1) },
            new TicketAssignment { TicketId = 22, AgentId = 1, Urgency = 0, AssignedAtUtc = oldest },
            new TicketAssignment { TicketId = 23, AgentId = 2, Urgency = 0, AssignedAtUtc = oldest });
        context.SaveChanges();

        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory, userId: 7);

        var result = Assert.IsType<OkObjectResult>(controller.GetQueue());
        var queue = Assert.IsAssignableFrom<IEnumerable<object>>(result.Value);
        var ticketIds = queue.Cast<Assignment.Api.DTO.AssignmentQueueItemDTO>().Select(item => item.TicketId);

        Assert.Equal(new[] { 22, 21, 20 }, ticketIds);
    }

    [Fact]
    public void GetQueue_WhenCallerHasNoAgentRow_ReturnsEmptyOkResult()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory, userId: 999);

        var result = Assert.IsType<OkObjectResult>(controller.GetQueue());

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Assignment.Api.DTO.AssignmentQueueItemDTO>>(result.Value));
        logger.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetQueue_WhenContextFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory, userId: 7);

        var result = Assert.IsType<ObjectResult>(controller.GetQueue());

        Assert.Equal(500, result.StatusCode);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
