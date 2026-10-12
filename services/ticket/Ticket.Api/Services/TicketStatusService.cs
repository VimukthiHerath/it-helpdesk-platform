using Ticket.Api.Data;
using Ticket.Api.Model;

namespace Ticket.Api.Services;

// SCRUM-17. Kept separate from the controller so the actual "does this
// write both the ticket and the history row correctly" logic can be unit
// tested without HTTP or role concerns involved - those stay in
// TicketController, same split as every other Service/Controller pair in
// this codebase.
public class TicketStatusService
{
    // AC1: the only statuses this endpoint will move a ticket into.
    // Unassigned/Assigned stay system-managed (round-robin, reassignment) -
    // not something an agent sets by hand through this endpoint.
    public static readonly IReadOnlySet<TicketStatus> SettableStatuses = new HashSet<TicketStatus>
    {
        TicketStatus.in_progress,
        TicketStatus.resolved,
        TicketStatus.closed,
    };

    private readonly ApplicationDbContext _context;

    public TicketStatusService(ApplicationDbContext context)
    {
        _context = context;
    }

    // AC3: updates the ticket and appends a history row in the same
    // SaveChangesAsync call, so the two can never drift out of sync with
    // each other (one succeeds, the other silently doesn't).
    public async Task<Tickets> ApplyStatusChangeAsync(Tickets ticket, TicketStatus newStatus, int changedBy, CancellationToken cancellationToken)
    {
        var oldStatus = ticket.Status;

        ticket.Status = newStatus;
        ticket.UpdatedAt = DateTime.UtcNow;

        _context.StatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedBy = changedBy,
        });

        await _context.SaveChangesAsync(cancellationToken);

        return ticket;
    }
}
