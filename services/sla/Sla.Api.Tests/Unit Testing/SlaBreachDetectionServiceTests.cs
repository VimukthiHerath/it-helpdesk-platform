using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sla.Api.Data;
using Sla.Api.Models;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests.UnitTesting;

public class SlaBreachDetectionServiceTests
{
    [Fact]
    public async Task CheckForBreachesAsync_ExpiredActiveTicket_PublishesAndMarksBreached()
    {
        await using var context = CreateContext();
        context.TicketSlas.Add(new TicketSla
        {
            TicketId = 17,
            Urgency = 1,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-8),
            DeadlineUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        producer.Setup(x => x.ProduceAsync(
                "sla-breached",
                It.Is<Message<string, string>>(message =>
                    message.Key == "17" &&
                    message.Value.Contains("\"ticketId\":17")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDeliveryResult());
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        producer.Verify(x => x.ProduceAsync(
            "sla-breached",
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Breached", (await context.TicketSlas.SingleAsync()).Status);
    }

    [Fact]
    public async Task CheckForBreachesAsync_NoExpiredActiveTickets_DoesNotPublishOrSave()
    {
        await using var context = CreateContext();
        context.TicketSlas.Add(new TicketSla
        {
            TicketId = 18,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(10),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Active", (await context.TicketSlas.SingleAsync()).Status);
    }

    [Fact]
    public async Task CheckForBreachesAsync_NonActiveExpiredTicket_DoesNotPublish()
    {
        await using var context = CreateContext();
        context.TicketSlas.Add(new TicketSla
        {
            TicketId = 19,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = "Breached"
        });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckForBreachesAsync_PublishFails_LeavesTicketActiveAndContinues()
    {
        await using var context = CreateContext();
        context.TicketSlas.Add(new TicketSla
        {
            TicketId = 20,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Kafka unavailable"));
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        Assert.Equal("Active", (await context.TicketSlas.SingleAsync()).Status);
    }

    private static SlaBreachDetectionService CreateService(
        SlaDbContext context,
        Mock<IProducer<string, string>> producer)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:SlaBreachedTopic"] = "sla-breached"
            })
            .Build();
        return new SlaBreachDetectionService(
            context,
            producer.Object,
            configuration,
            new Mock<ILogger<SlaBreachDetectionService>>().Object);
    }

    private static Mock<IProducer<string, string>> CreateProducer()
    {
        return new Mock<IProducer<string, string>>();
    }

    private static DeliveryResult<string, string> CreateDeliveryResult()
    {
        return new DeliveryResult<string, string>
        {
            Topic = "sla-breached",
            Partition = new Partition(0),
            Offset = new Offset(1)
        };
    }

    private static SlaDbContext CreateContext()
    {
        return new SlaDbContext(new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
