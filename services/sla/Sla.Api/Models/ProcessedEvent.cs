namespace Sla.Api.Models;

public class ProcessedEvent
{
    public int Id { get; set; }
    
    // Uniquely identifies the Kafka event (e.g. "TicketCreated-0-15")
    public string EventKey { get; set; } = string.Empty;
    
    public DateTime ProcessedAtUtc { get; set; }
}
