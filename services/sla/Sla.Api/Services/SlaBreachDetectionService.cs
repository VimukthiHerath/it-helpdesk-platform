using System.Text.Json;
using Confluent.Kafka;
using Sla.Api.Data;
using Sla.Api.DTO;

namespace Sla.Api.Services;

// SLA-3: the actual breach-detection logic (AC1/AC2/AC3), kept separate from
// SlaBreachMonitorService's timer loop so it can be unit-tested without a
// real Kafka broker or MySQL instance - same pattern as Assignment.Api's
// RoundRobinAssignmentService.
public class SlaBreachDetectionService
{
    private readonly SlaDbContext _db;
    private readonly IProducer<string, string> _producer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SlaBreachDetectionService> _logger;

    public SlaBreachDetectionService(
        SlaDbContext db,
        IProducer<string, string> producer,
        IConfiguration configuration,
        ILogger<SlaBreachDetectionService> logger)
    {
        _db = db;
        _producer = producer;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task CheckForBreachesAsync(CancellationToken cancellationToken)
    {
        var topic = _configuration["Kafka:SlaBreachedTopic"] ?? "sla-breached";

        // 1. Find all active tickets whose deadline has passed
        var breachedTickets = _db.TicketSlas
            .Where(t => t.Status == "Active" && t.DeadlineUtc <= DateTime.UtcNow)
            .ToList();

        if (!breachedTickets.Any())
        {
            return;
        }

        _logger.LogWarning("Found {Count} breached tickets!", breachedTickets.Count);

        // 2. Process each breached ticket
        foreach (var ticket in breachedTickets)
        {
            var eventPayload = new SlaBreachedEvent
            {
                TicketId = ticket.TicketId,
                OriginalDeadlineUtc = ticket.DeadlineUtc,
                BreachedAtUtc = DateTime.UtcNow
            };

            var messageString = JsonSerializer.Serialize(eventPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            try
            {
                var result = await _producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = ticket.TicketId.ToString(),
                    Value = messageString
                }, cancellationToken);

                _logger.LogInformation("Published SlaBreached event for Ticket {TicketId}. Partition: {Partition}, Offset: {Offset}",
                    ticket.TicketId, result.Partition, result.Offset);

                // 3. Update the in-memory Status so we don't alarm twice - not
                // persisted until the single SaveChangesAsync below (see
                // BUG regression test: SaveChangesFailsAfterPublish_RepublishesSameTicketOnNextCycle).
                ticket.Status = "Breached";
            }
            catch (ProduceException<string, string> ex)
            {
                // If Kafka fails, log and continue to the next ticket.
                // Status is NOT updated for this ticket, so it will be retried next cycle (AC3).
                _logger.LogError(ex, "Failed to publish SLA breach for Ticket {TicketId}. Reason: {Reason}", ticket.TicketId, ex.Error.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing SLA breach for Ticket {TicketId}.", ticket.TicketId);
            }
        }

        // 4. Save all successful status updates - a single call for the whole
        // batch. If this throws after some tickets already published
        // successfully to Kafka, every one of those in-memory Status changes
        // is lost, and the next cycle will republish them (see BUG regression test).
        await _db.SaveChangesAsync(cancellationToken);
    }
}
