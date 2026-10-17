using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
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
}
