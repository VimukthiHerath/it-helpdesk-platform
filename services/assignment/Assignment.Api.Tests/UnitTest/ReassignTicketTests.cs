using Assignment.Api.DTO;
using Assignment.Api.Model;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class ReassignTicketTests
{
    [Fact]
    public async Task ReassignTicket_WhenModelStateIsInvalid_ReturnsValidationProblemWithoutPublishing()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);
        controller.ModelState.AddModelError(nameof(ReassignTicketDTO.NewAgentUserId), "NewAgentUserId is required.");

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO());

        Assert.IsType<ObjectResult>(result);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignTicket_WhenTargetIsInRotation_UpdatesStateAndPublishesEvent()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var originalTime = DateTime.UtcNow.AddHours(-1);
        context.Agents.AddRange(new Agent { Id = 1, UserId = 10 }, new Agent { Id = 2, UserId = 20 });
        context.Assignments.Add(new TicketAssignment
        {
            TicketId = 55, AgentId = 1, Urgency = 2, AssignedAtUtc = originalTime
        });
        context.SaveChanges();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryResult<string, string>)null!);
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO { NewAgentUserId = 20 });

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ReassignTicketResponseDTO>(response.Value);
        Assert.Equal(55, body.TicketId);
        Assert.Equal(20, body.AgentUserId);
        var assignment = context.Assignments.Single();
        Assert.Equal(2, assignment.AgentId);
        Assert.True(assignment.AssignedAtUtc > originalTime);
        producer.Verify(x => x.ProduceAsync(
            "ticket-assigned", It.Is<Message<string, string>>(message =>
                message.Key == "55" && message.Value.Contains("\"agentUserId\":20")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReassignTicket_WhenTicketDoesNotExist_ReturnsNotFoundWithoutPublishing()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO { NewAgentUserId = 20 });

        Assert.IsType<NotFoundObjectResult>(result);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignTicket_WhenTargetIsNotInRotation_ReturnsBadRequestWithoutChangingState()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.Add(new Agent { Id = 1, UserId = 10 });
        context.Assignments.Add(new TicketAssignment { TicketId = 55, AgentId = 1, Urgency = 2 });
        context.SaveChanges();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO { NewAgentUserId = 20 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(1, context.Assignments.Single().AgentId);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignTicket_WhenTargetIsCurrentAgent_ReturnsConflictWithoutPublishing()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.Add(new Agent { Id = 1, UserId = 10 });
        context.Assignments.Add(new TicketAssignment { TicketId = 55, AgentId = 1, Urgency = 2 });
        context.SaveChanges();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO { NewAgentUserId = 10 });

        Assert.IsType<ConflictObjectResult>(result);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignTicket_WhenPublishingFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.AddRange(new Agent { Id = 1, UserId = 10 }, new Agent { Id = 2, UserId = 20 });
        context.Assignments.Add(new TicketAssignment { TicketId = 55, AgentId = 1, Urgency = 2 });
        context.SaveChanges();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Kafka unavailable"));
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.ReassignTicket(55, new ReassignTicketDTO { NewAgentUserId = 20 });

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(2, context.Assignments.Single().AgentId);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
