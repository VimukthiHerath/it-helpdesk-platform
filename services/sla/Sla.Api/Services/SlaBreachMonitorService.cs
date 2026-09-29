namespace Sla.Api.Services;

public sealed class SlaBreachMonitorService : BackgroundService
{
    private readonly ILogger<SlaBreachMonitorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public SlaBreachMonitorService(
        ILogger<SlaBreachMonitorService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SLA Breach Monitor Service starting.");
        
        // Wait 10 seconds before the first run so the app has time to start completely.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Run exactly every 1 minute
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("Running SLA breach check at {Time}", DateTime.UtcNow);

                using var scope = _scopeFactory.CreateScope();
                var detectionService = scope.ServiceProvider.GetRequiredService<SlaBreachDetectionService>();
                await detectionService.CheckForBreachesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SLA Breach Monitor Service is stopping.");
        }
    }
}
