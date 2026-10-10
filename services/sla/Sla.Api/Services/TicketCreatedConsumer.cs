using System.Text.Json;
using Sla.Api.DTO;
using Confluent.Kafka;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Sla.Api.Data;
using Sla.Api.Model;

namespace Sla.Api.Services;

public sealed class TicketCreatedConsumer : BackgroundService
{
    private const string ConsumerGroupId = "sla-timers";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketCreatedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _memoryCache;

    public TicketCreatedConsumer(
        IConfiguration configuration,
        ILogger<TicketCreatedConsumer> logger,
        IServiceScopeFactory scopeFactory,
        IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _memoryCache = memoryCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield to allow hosted services to start up gracefully if Consume blocks
        await Task.Yield();

        var bootstrapServers =
            _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var topic =
            _configuration["Kafka:TicketCreatedTopic"] ?? "ticket-created";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(
            consumerConfig).Build();

        consumer.Subscribe(topic);

        _logger.LogInformation(
            "SLA timer consumer started. Topic={Topic}, GroupId={GroupId}, BootstrapServers={BootstrapServers}",
            topic,
            ConsumerGroupId,
            bootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    if (string.IsNullOrWhiteSpace(result.Message.Value))
                    {
                        _logger.LogWarning(
                            "Received an empty TicketCreated message. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    var ticketEvent =
                        JsonSerializer.Deserialize<TicketCreatedEvent>(
                            result.Message.Value,
                            new JsonSerializerOptions(
                                JsonSerializerDefaults.Web));

                    if (ticketEvent is null)
                    {
                        _logger.LogWarning(
                            "Received an invalid TicketCreated event. Partition={Partition}, Offset={Offset}",
                            result.Partition,
                            result.Offset);

                        continue;
                    }

                    _logger.LogInformation(
                        "Received TicketCreated event. " +
                        "TicketId={TicketId}, Description={Description}, " +
                        "IssueType={IssueType}, Urgency={Urgency}, " +
                        "Status={Status}, CreatedBy={CreatedBy}, " +
                        "CreatedAtUtc={CreatedAtUtc}, Partition={Partition}, Offset={Offset}",
                        ticketEvent.TicketId,
                        ticketEvent.Description,
                        ticketEvent.IssueType,
                        ticketEvent.Urgency,
                        ticketEvent.Status,
                        ticketEvent.CreatedBy,
                        ticketEvent.CreatedAtUtc,
                        result.Partition,
                        result.Offset);

                    // SLA Calculation & Database Insertion
                    await ProcessSlaAsync(ticketEvent);
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(
                        exception,
                        "Kafka consume error: {Reason}",
                        exception.Error.Reason);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Received malformed TicketCreated event");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "SLA timer Kafka consumer is shutting down.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessSlaAsync(TicketCreatedEvent ticketEvent)
    {
        string urgencyStr = ticketEvent.Urgency switch
        {
            1 => "LOW",
            2 => "MEDIUM",
            3 => "HIGH",
            4 => "CRITICAL",
            _ => "MEDIUM"
        };
        var cacheKey = $"SlaTier_{urgencyStr}";

        if (!_memoryCache.TryGetValue(cacheKey, out int durationMinutes))
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SlaDbContext>();

            var policy = await dbContext.SlaTierPolicies
                .FirstOrDefaultAsync(p => p.TierName == urgencyStr);

            durationMinutes = policy?.DurationMinutes ?? 1440; // Default fallback

            _memoryCache.Set(cacheKey, durationMinutes, TimeSpan.FromMinutes(30));
        }

        var deadline = ticketEvent.CreatedAtUtc.AddMinutes(durationMinutes);

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SlaDbContext>();
            var newSla = new TicketSla
            {
                TicketId = ticketEvent.TicketId,
                TargetResolutionTimeUtc = deadline
            };

            dbContext.TicketSlas.Add(newSla);
            await dbContext.SaveChangesAsync();
        }
        
        _logger.LogInformation("Calculated SLA deadline {Deadline} (Duration {DurationMinutes}m) for Ticket {TicketId}",
            deadline, durationMinutes, ticketEvent.TicketId);
    }
}