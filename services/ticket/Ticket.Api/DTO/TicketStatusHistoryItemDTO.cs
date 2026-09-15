using Ticket.Api.Model;

namespace Ticket.Api.DTO;

public class TicketStatusHistoryItemDTO
{
    public TicketStatus OldStatus { get; set; }
    public TicketStatus NewStatus { get; set; }
    public int ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
