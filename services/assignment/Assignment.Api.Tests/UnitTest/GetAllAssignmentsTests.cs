using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class GetAllAssignmentsTests
{
    [Fact]
    public void GetAllAssignments_ReturnsJoinedAssignmentsSortedByTicketId()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.AddRange(new Agent { Id = 1, UserId = 10 }, new Agent { Id = 2, UserId = 20 });
        context.Assignments.AddRange(
            new TicketAssignment { TicketId = 200, AgentId = 2, Urgency = 2 },
            new TicketAssignment { TicketId = 100, AgentId = 1, Urgency = 0 });
        context.SaveChanges();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = Assert.IsType<OkObjectResult>(controller.GetAllAssignments());
        var assignments = Assert.IsAssignableFrom<IEnumerable<Assignment.Api.DTO.AssignmentListItemDTO>>(result.Value);

        Assert.Equal(new[] { 100, 200 }, assignments.Select(assignment => assignment.TicketId));
        Assert.Equal(new[] { 10, 20 }, assignments.Select(assignment => assignment.AgentUserId));
    }

    [Fact]
    public void GetAllAssignments_WhenContextFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = Assert.IsType<ObjectResult>(controller.GetAllAssignments());

        Assert.Equal(500, result.StatusCode);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
