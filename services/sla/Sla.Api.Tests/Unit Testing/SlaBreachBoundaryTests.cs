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

public class SlaBreachBoundaryTests
{
    [Fact]
    public async Task CheckForBreachesAsync_DeadlineAtOrBeforeNow_IsEligible()
    {
        await using var context = CreateContext();
        context.TicketSlas.Add(new TicketSla
        {
            TicketId = 1,
            Status = "Active",
            DeadlineUtc = DateTime.UtcNow.AddMilliseconds(-1)
        });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        Assert.Equal("Breached", (await context.TicketSlas.SingleAsync()).Status);
    }

    [Fact]
    public async Task CheckForBreachesAsync_MixedPublishResults_ContinuesAndMarksOnlySuccessfulTickets()
    {
        await using var context = CreateContext();
        context.TicketSlas.AddRange(
            new TicketSla { TicketId = 1, Status = "Active", DeadlineUtc = DateTime.UtcNow.AddMinutes(-2) },
            new TicketSla { TicketId = 2, Status = "Active", DeadlineUtc = DateTime.UtcNow.AddMinutes(-1) });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(),
                It.Is<Message<string, string>>(m => m.Key == "1"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Kafka unavailable"));
        var service = CreateService(context, producer);

        await service.CheckForBreachesAsync(CancellationToken.None);

        var records = await context.TicketSlas.OrderBy(x => x.TicketId).ToListAsync();
        Assert.Equal("Active", records[0].Status);
        Assert.Equal("Breached", records[1].Status);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static SlaBreachDetectionService CreateService(
        SlaDbContext context, Mock<IProducer<string, string>> producer) =>
        new(
            context,
            producer.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:SlaBreachedTopic"] = "sla-breached"
            }).Build(),
            new Mock<ILogger<SlaBreachDetectionService>>().Object);

    private static Mock<IProducer<string, string>> CreateProducer()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>
            {
                Topic = "sla-breached",
                Partition = new Partition(0),
                Offset = new Offset(1)
            });
        return producer;
    }

    private static SlaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
