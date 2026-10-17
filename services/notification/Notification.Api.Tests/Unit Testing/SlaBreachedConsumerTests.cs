using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class SlaBreachedConsumerTests
{
    [Fact]
    public async Task StartAsync_WhenCancellationIsAlreadyRequested_CompletesWithoutCreatingScope()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var consumer = new SlaBreachedConsumer(
            CreateConfiguration(),
            new Mock<ILogger<SlaBreachedConsumer>>().Object,
            scopeFactory.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            consumer.StartAsync(cancellation.Token));

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void SlaBreachedEvent_StoresBreachDetails()
    {
        var deadline = DateTime.UtcNow.AddHours(-1);
        var breachedAt = DateTime.UtcNow;

        var breachedEvent = new SlaBreachedEvent
        {
            TicketId = 27,
            OriginalDeadlineUtc = deadline,
            BreachedAtUtc = breachedAt
        };

        Assert.Equal(27, breachedEvent.TicketId);
        Assert.Equal(deadline, breachedEvent.OriginalDeadlineUtc);
        Assert.Equal(breachedAt, breachedEvent.BreachedAtUtc);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:9092",
                ["Kafka:SlaBreachedTopic"] = "sla-breached"
            })
            .Build();
    }
}
