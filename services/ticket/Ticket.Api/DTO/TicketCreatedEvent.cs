namespace Ticket.Api.Model;

public class TicketCreatedEvent
{
    public string EventType { get; set; } = "TicketCreated";
    public int TicketId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public TicketUrgency Urgency { get; set; }
    public TicketStatus Status { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}