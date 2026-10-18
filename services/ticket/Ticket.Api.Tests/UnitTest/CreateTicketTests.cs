using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
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
        Assert.Equal("Network", ticket.IssueType);
        Assert.Equal(TicketUrgency.six_hours, ticket.Urgency);

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
            It.IsAny<string>(), 
            It.IsAny<Message<string, string>>(), 
            It.IsAny<CancellationToken>()), 
            Times.Never
        );
    }

    [Fact]
    public async Task CreateTicket_WhenFirstValidationErrorIsBlank_UsesFirstMeaningfulMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);
        controller.ModelState.AddModelError("first", " ");
        controller.ModelState.AddModelError("second", "Description is required.");

        var result = Assert.IsType<BadRequestObjectResult>(
            await controller.CreateTicket(new TicketCreateDTO()));

        Assert.Equal("Description is required.", GetProperty(result.Value!, "message"));
    }

    [Fact]
    public async Task CreateTicket_WhenAllValidationErrorsAreBlank_UsesFallbackMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);
        controller.ModelState.AddModelError("description", " ");

        var result = Assert.IsType<BadRequestObjectResult>(
            await controller.CreateTicket(new TicketCreateDTO()));

        Assert.Equal("Please check the ticket details and try again.",
            GetProperty(result.Value!, "message"));
    }

    [Fact]
    public async Task CreateTicket_WhenTopicIsMissing_UsesDefaultTopicAndSuccessMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), 
                It.IsAny<Message<string, string>>(), 
                It.IsAny<CancellationToken>()
                )
            ).ReturnsAsync((DeliveryResult<string, string>)null!);

        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = await controller.CreateTicket(new TicketCreateDTO
        {
            Description = "Default topic", 
            IssueType = "Network", 
            Urgency = TicketUrgency.one_hour
        });

        var created = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("Ticket created successfully", GetProperty(created.Value!, "message"));

        producer.Verify(x => x.ProduceAsync(
            "ticket-created", 
            It.IsAny<Message<string, string>>(), 
            It.IsAny<CancellationToken>()),
            Times.Once
        );

        VerifyLog(logger, LogLevel.Information,
            "Published TicketCreated event for ticket {TicketId} to topic {Topic}");
    }

    [Fact]
    public async Task CreateTicket_WhenUserIdIsInvalid_ReturnsInternalServerErrorWithRetryMessage()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService);

        var result = await controller.CreateTicket(new TicketCreateDTO
        {
            Description = "Invalid user", 
            IssueType = "Network", 
            Urgency = TicketUrgency.one_hour
        });

        var error = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        Assert.Equal("Something went wrong on our end. Please try again in a moment.",
            GetProperty(error.Value!, "message"));
            
        VerifyLog(logger, LogLevel.Error, "Error creating ticket");
    }

    [Fact]
    public async Task CreateTicket_WhenPublishingFails_Returns503WithMessagePersistsTicketAndLogsError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();

        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_TimedOut), null));

        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42);

        var result = await controller.CreateTicket(new TicketCreateDTO
        {
            Description = "Publish failure",
            IssueType = "Network",
            Urgency = TicketUrgency.one_hour
        });

        var error = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        Assert.Equal("Ticket created, but failed to publish TicketCreated event.",
            GetProperty(error.Value!, "message"));
        Assert.Single(context.Tickets);

        VerifyLog(logger, LogLevel.Error, "Ticket created in DB, but failed to publish TicketCreated event");

        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
