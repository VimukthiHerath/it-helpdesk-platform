using System.ComponentModel.DataAnnotations;

namespace Ticket.Api.Model;

public class TicketCreateDTO
{
    [Required]
    [MaxLength(255)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string IssueType { get; set; } = string.Empty;

    [Required]
    public TicketUrgency Urgency { get; set; } = TicketUrgency.one_hour;
}