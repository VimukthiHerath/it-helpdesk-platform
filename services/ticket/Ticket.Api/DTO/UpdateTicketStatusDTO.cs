using System.ComponentModel.DataAnnotations;
using Ticket.Api.Model;

namespace Ticket.Api.DTO;

public class UpdateTicketStatusDTO
{
    [Required]
    public TicketStatus NewStatus { get; set; }
}
