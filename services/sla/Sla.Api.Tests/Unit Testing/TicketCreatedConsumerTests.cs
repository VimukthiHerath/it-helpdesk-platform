using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests.UnitTesting;

public class TicketCreatedConsumerTests
{
    [Fact]
    public async Task StartAsync_WhenAlreadyCanceled_StopsWithoutCreatingScope()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var consumer = new TicketCreatedConsumer(
            CreateConfiguration(),
            new Mock<ILogger<TicketCreatedConsumer>>().Object,
            scopeFactory.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await consumer.StartAsync(cancellation.Token);

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
        await consumer.StopAsync(CancellationToken.None);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:9092",
                ["Kafka:TicketCreatedTopic"] = "ticket-created"
            })
            .Build();
    }
}
