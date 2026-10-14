namespace Ticket.Api.DTO;

public class TicketReportItemDTO
{
    public int TicketId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Urgency { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public int? AssignedAgent { get; set; }
}
