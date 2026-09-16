using Ticket.Api.Model;

namespace Ticket.Api.DTO;

public class TicketStatusResponseDTO
{
    public int TicketId { get; set; }
    public TicketStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; }
}
