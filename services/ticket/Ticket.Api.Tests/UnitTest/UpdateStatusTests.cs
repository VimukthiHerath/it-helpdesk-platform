using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class UpdateStatusTests
{
    [Fact]
    public async Task UpdateStatus_WhenAssignedAgentChangesStatus_UpdatesTicketAndHistory()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Issue", IssueType = "Software",
            CreatedBy = 1, AssignedTo = 42, Status = TicketStatus.assigned
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.in_progress });

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<TicketStatusResponseDTO>(response.Value);
        Assert.Equal(TicketStatus.in_progress, body.Status);
        Assert.Equal(TicketStatus.in_progress, context.Tickets.Single().Status);
        var history = Assert.Single(context.StatusHistory);
        Assert.Equal(TicketStatus.assigned, history.OldStatus);
        Assert.Equal(42, history.ChangedBy);
    }

    [Fact]
    public async Task UpdateStatus_WhenTicketDoesNotExist_ReturnsNotFoundWithoutChangingState()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.resolved });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(context.StatusHistory);
    }

    [Fact]
    public async Task UpdateStatus_WhenAgentIsNotAssigned_ReturnsForbidden()
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

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.resolved });

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Empty(context.StatusHistory);
    }

    [Fact]
    public async Task UpdateStatus_WhenStatusIsNotSettable_ReturnsBadRequestWithoutCallingService()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.assigned });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(context.StatusHistory);
    }

    [Fact]
    public async Task UpdateStatus_WhenModelStateIsInvalid_ReturnsBadRequest()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });
        controller.ModelState.AddModelError(nameof(UpdateTicketStatusDTO.NewStatus), "Status is required.");

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateStatus_WhenAdministratorActsOnAnotherUsersTicket_Succeeds()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Issue", IssueType = "Software",
            CreatedBy = 1, AssignedTo = 99, Status = TicketStatus.assigned
        });
        await context.SaveChangesAsync();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.resolved });

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<TicketStatusResponseDTO>(response.Value);
        Assert.Equal(7, body.TicketId);
        Assert.Equal(TicketStatus.resolved, body.Status);
        Assert.Equal(42, Assert.Single(context.StatusHistory).ChangedBy);
    }

    [Fact]
    public async Task UpdateStatus_WhenDatabaseFails_ReturnsInternalServerError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(
            7, new UpdateTicketStatusDTO { NewStatus = TicketStatus.resolved });

        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<ObjectResult>(result).StatusCode);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_WhenModelStateIsInvalid_ReturnsExactMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);
        controller.ModelState.AddModelError("status", "required");

        var result = Assert.IsType<BadRequestObjectResult>(
            await controller.UpdateStatus(1, new UpdateTicketStatusDTO()));

        Assert.Equal("A valid status is required.", GetProperty(result.Value!, "message"));
    }

    [Fact]
    public async Task UpdateStatus_WhenStatusIsNotSettable_ReturnsExactMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = Assert.IsType<BadRequestObjectResult>(
            await controller.UpdateStatus(1, new UpdateTicketStatusDTO
            {
                NewStatus = TicketStatus.assigned
            }));

        Assert.Equal("Status must be In Progress, Resolved, or Closed.",
            GetProperty(result.Value!, "message"));
    }

    [Fact]
    public async Task UpdateStatus_WhenClosed_UsesDefaultTopicAndPublishesCompleteEvent()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryResult<string, string>)null!);
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Close me", CreatedBy = 1, AssignedTo = 42,
            Status = TicketStatus.in_progress
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(7, new UpdateTicketStatusDTO
        {
            NewStatus = TicketStatus.closed
        });

        Assert.IsType<OkObjectResult>(result);
        producer.Verify(x => x.ProduceAsync("ticket-resolved",
            It.Is<Message<string, string>>(message =>
                message.Key == "7" &&
                message.Value.Contains("\"ticketId\":7") &&
                message.Value.Contains("\"resolvedByUserId\":\"42\"") &&
                message.Value.Contains("\"status\":\"closed\"")),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyLog(logger, LogLevel.Information, "Published TicketResolvedEvent for Ticket {TicketId}");
    }

    [Fact]
    public async Task UpdateStatus_WhenResolvedTopicIsConfigured_UsesConfiguredTopic()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        configuration.SetupGet(x => x["Kafka:TicketResolvedTopic"]).Returns("ticket-resolved-test");
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryResult<string, string>)null!);
        context.Tickets.Add(new Tickets
        {
            Id = 8, Description = "Resolve me", CreatedBy = 1, AssignedTo = 42,
            Status = TicketStatus.in_progress
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        await controller.UpdateStatus(8, new UpdateTicketStatusDTO
        {
            NewStatus = TicketStatus.resolved
        });

        producer.Verify(x => x.ProduceAsync("ticket-resolved-test",
            It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_WhenPublishingFails_ReturnsExactErrorAndLogsExpectedMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));
        context.Tickets.Add(new Tickets
        {
            Id = 7, Description = "Publish failure", CreatedBy = 1, AssignedTo = 42,
            Status = TicketStatus.assigned
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Agent" });

        var result = await controller.UpdateStatus(7, new UpdateTicketStatusDTO
        {
            NewStatus = TicketStatus.resolved
        });

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        Assert.Equal("Something went wrong on our end. Please try again in a moment.",
            GetProperty(error.Value!, "message"));
        VerifyLog(logger, LogLevel.Error, "Error updating status for ticket {TicketId}");
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
