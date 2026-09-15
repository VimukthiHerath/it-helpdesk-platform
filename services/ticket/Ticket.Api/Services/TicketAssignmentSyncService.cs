using Microsoft.EntityFrameworkCore;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;

namespace Ticket.Api.Services;

// Keeps Tickets.Status/AssignedTo in sync with Assignment.Api's own view of
// who has each ticket. Before this, nothing consumed TicketAssigned on the
// Ticket side - Assignment published it (for round-robin, then again for
// SCRUM-20's manual reassignment), but Ticket.Api's own Status/AssignedTo
// columns just sat at their creation-time defaults forever. Same event
// handles both the original assignment and any later reassignment - this
// service doesn't need to know or care which one it's looking at, it just
// applies whatever the latest event says.
//
// Kept separate from the Kafka-consuming BackgroundService so it can be
// unit-tested with EF Core's InMemory provider, no broker needed - same
// split as RoundRobinAssignmentService/TicketCreatedConsumer in Assignment.Api.
public class TicketAssignmentSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TicketAssignmentSyncService> _logger;

    public TicketAssignmentSyncService(ApplicationDbContext context, ILogger<TicketAssignmentSyncService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ApplyAsync(TicketAssignedEvent assignedEvent, CancellationToken cancellationToken)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == assignedEvent.TicketId, cancellationToken);

        if (ticket is null)
        {
            // Shouldn't happen in practice (Assignment only assigns tickets
            // that came from a real TicketCreated event) but a missing ticket
            // here isn't a reason to crash the consumer loop for every event
            // after it.
            _logger.LogWarning(
                "Received TicketAssigned for ticket {TicketId}, but no such ticket exists. Skipping.",
                assignedEvent.TicketId);
            return;
        }

        ticket.AssignedTo = assignedEvent.AgentUserId;
        ticket.Status = TicketStatus.assigned;
        ticket.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synced ticket {TicketId}: now assigned to agent user {AgentUserId}",
            assignedEvent.TicketId, assignedEvent.AgentUserId);
    }
}
