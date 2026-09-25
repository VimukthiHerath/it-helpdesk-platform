namespace Sla.Api.Models;

public class TicketSla
{
    public int Id { get; set; }
    
    public int TicketId { get; set; }
    
    public int Urgency { get; set; }
    
    public DateTime CreatedAtUtc { get; set; }
    
    public DateTime DeadlineUtc { get; set; }
    
    public string Status { get; set; } = "Active"; // Active, Breached, Met
}
