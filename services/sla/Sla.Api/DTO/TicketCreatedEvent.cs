namespace Sla.Api.DTO;

public class TicketCreatedEvent
{
    public string EventType { get; set; } = "TicketCreated";
    public int TicketId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public int Urgency { get; set; }
    public int Status { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}