namespace Sla.Api.DTO;

public class SlaBreachedEvent
{
    public int TicketId { get; set; }
    
    public DateTime OriginalDeadlineUtc { get; set; }
    
    public DateTime BreachedAtUtc { get; set; }
}
