using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Sla.Api.Data;
using Sla.Api.Events;

namespace Sla.Api.Consumers;

public sealed class TicketResolvedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "sla-service-resolved-group";
    private const string EventType = "TicketResolved";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketResolvedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketResolvedConsumer(
        IConfiguration configuration,
        ILogger<TicketResolvedConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private async Task RunConsumerLoop(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _configuration["Kafka:TicketResolvedTopic"] ?? "ticket-resolved";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Enforce manual commit after DB write per BUG-4 fixes
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation("TicketResolvedConsumer started. Topic={Topic}, GroupId={GroupId}", topic, ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    if (string.IsNullOrWhiteSpace(result.Message.Value))
                    {
                        continue;
                    }

                    var resolvedEvent = JsonSerializer.Deserialize<TicketResolvedEvent>(
                        result.Message.Value,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));

                    if (resolvedEvent == null)
                    {
                        continue;
                    }

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<SlaDbContext>();

                        var slaRecord = await dbContext.TicketSlas
                            .FirstOrDefaultAsync(s => s.TicketId == resolvedEvent.TicketId && s.Status == "Active", stoppingToken);

                        if (slaRecord != null)
                        {
                            slaRecord.Status = "Resolved";
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                        else
                        {
                            _logger.LogWarning("No active SLA record found for resolved Ticket {TicketId}. Record may have already been resolved or breached.", resolvedEvent.TicketId);
                        }

                        // Explicitly commit manually after a successful loop
                        consumer.Commit(result);
                    }
                    catch (Exception ex)
                    {
                        // Ensure it fails gracefully, swallowing crash loop and letting consumer stall rather than crashing the API.
                        // Bug-4 resilience principles state to avoid loop crashing on failures, let them drop to avoid blocking host. wait
                        // "Ensure manual offset commits are executed only after SaveChangesAsync() succeeds."
                        _logger.LogError(ex, "Failed resolving SLA for Ticket {TicketId}", resolvedEvent.TicketId);
                    }
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Kafka consume error: {Reason}", exception.Error.Reason);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(exception, "Received malformed TicketResolved event");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("TicketResolvedConsumer shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }
}
