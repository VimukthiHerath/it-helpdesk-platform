using Sla.Api.Data;
using Sla.Api.DTO;
using Sla.Api.Models;

namespace Sla.Api.Services;

// SLA-2: creates the SLA record for a newly-created ticket (AC1 deadline
// calculation + AC2 persistence, plus the idempotency check), kept separate
// from the Kafka consumer so it's unit-testable without a broker or MySQL.
public class SlaRecordCreationService
{
    private readonly SlaDbContext _db;
    private readonly ILogger<SlaRecordCreationService> _logger;

    public SlaRecordCreationService(SlaDbContext db, ILogger<SlaRecordCreationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CreateAsync(TicketCreatedEvent ticketEvent, string eventKey, CancellationToken cancellationToken)
    {
        var alreadyProcessed = _db.ProcessedEvents.Any(e => e.EventKey == eventKey);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Event {EventKey} already processed. Skipping.", eventKey);
            return;
        }

        var deadlineUtc = SlaDeadlineCalculator.ComputeDeadline(ticketEvent.CreatedAtUtc, ticketEvent.Urgency);

        _db.TicketSlas.Add(new TicketSla
        {
            TicketId = ticketEvent.TicketId,
            Urgency = ticketEvent.Urgency,
            CreatedAtUtc = ticketEvent.CreatedAtUtc,
            DeadlineUtc = deadlineUtc,
            Status = "Active"
        });

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventKey = eventKey,
            ProcessedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully calculated and saved SLA deadline for Ticket {TicketId}. DeadlineUtc={DeadlineUtc}",
            ticketEvent.TicketId, deadlineUtc);
    }
}
