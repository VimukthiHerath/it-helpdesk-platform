namespace Ticket.Api.Model;

public enum TicketStatus
{
    unassigned = 0,
    assigned = 1,
    resolved = 2,
    // Added for SCRUM-17 - new ordinals, not renumbered, since Status is
    // stored as a plain int column and existing rows already hold 0/1/2.
    in_progress = 3,
    closed = 4,
}