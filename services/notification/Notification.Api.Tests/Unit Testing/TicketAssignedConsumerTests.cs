using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class TicketAssignedConsumerTests
{
    [Fact]
    public async Task StartAsync_WhenCancellationIsAlreadyRequested_CompletesWithoutCreatingScope()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var consumer = new TicketAssignedConsumer(
            CreateConfiguration(),
            new Mock<ILogger<TicketAssignedConsumer>>().Object,
            scopeFactory.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            consumer.StartAsync(cancellation.Token));

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
        await consumer.StopAsync(CancellationToken.None);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:9092",
                ["Kafka:TicketAssignedTopic"] = "ticket-assigned"
            })
            .Build();
    }
}
