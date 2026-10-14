using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class GetAgentsTests
{
    [Fact]
    public void GetAgents_ReturnsAgentsSortedByDisplayOrder()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.AddRange(
            new Agent { Id = 1, UserId = 11, DisplayOrder = 2 },
            new Agent { Id = 2, UserId = 12, DisplayOrder = 1 });
        context.SaveChanges();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = Assert.IsType<OkObjectResult>(controller.GetAgents());
        var agents = Assert.IsAssignableFrom<IEnumerable<Assignment.Api.DTO.AgentDTO>>(result.Value);

        Assert.Equal(new[] { 12, 11 }, agents.Select(agent => agent.UserId));
    }

    [Fact]
    public void GetAgents_WhenContextFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = Assert.IsType<ObjectResult>(controller.GetAgents());

        Assert.Equal(500, result.StatusCode);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
