using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class CreateTicketTests
{
    [Fact]
    public async Task CreateTicket_WhenValid_PersistsTicketAndPublishesEvent()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        configuration.SetupGet(x => x["Kafka:TicketCreatedTopic"]).Returns("ticket-created-test");
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryResult<string, string>)null!);
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = await controller.CreateTicket(new TicketCreateDTO
        {
            Description = "Cannot connect",
            IssueType = "Network",
            Urgency = TicketUrgency.six_hours
        });

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var ticket = Assert.Single(context.Tickets);
        Assert.Equal(42, ticket.CreatedBy);
        Assert.Equal("Cannot connect", ticket.Description);
        producer.Verify(x => x.ProduceAsync(
            "ticket-created-test",
            It.Is<Message<string, string>>(message =>
                message.Key == ticket.Id.ToString() &&
                message.Value.Contains("\"description\":\"Cannot connect\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTicket_WhenModelStateIsInvalid_ReturnsBadRequestWithoutSavingOrPublishing()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);
        controller.ModelState.AddModelError(nameof(TicketCreateDTO.Description), "Description is required.");

        var result = await controller.CreateTicket(new TicketCreateDTO());

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(context.Tickets);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTicket_WhenPublishingFails_ReturnsServiceUnavailableWithCreatedTicketId()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_TimedOut), null));
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = await controller.CreateTicket(new TicketCreateDTO
        {
            Description = "Printer issue",
            IssueType = "Hardware",
            Urgency = TicketUrgency.one_hour
        });

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        Assert.Single(context.Tickets);
        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
