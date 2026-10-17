using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests.UnitTesting;

public class SlaBreachMonitorServiceTests
{
    [Fact]
    public async Task StartAsync_WhenAlreadyCanceled_StopsWithoutCreatingScope()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new SlaBreachMonitorService(
            new Mock<ILogger<SlaBreachMonitorService>>().Object,
            scopeFactory.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            service.StartAsync(cancellation.Token));

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
        await service.StopAsync(CancellationToken.None);
    }
}
