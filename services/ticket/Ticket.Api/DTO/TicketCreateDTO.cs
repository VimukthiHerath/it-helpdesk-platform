using System.ComponentModel.DataAnnotations;

namespace Ticket.Api.Model;

public class TicketCreateDTO
{
    // AC1: each message names its own field and the specific problem, so
    // the controller can surface it as-is instead of a generic fallback.
    [Required(ErrorMessage = "Description is required.")]
    [MaxLength(255, ErrorMessage = "Description must be 255 characters or fewer.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Issue type is required.")]
    [MaxLength(100, ErrorMessage = "Issue type must be 100 characters or fewer.")]
    public string IssueType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Urgency is required.")]
    public TicketUrgency Urgency { get; set; } = TicketUrgency.one_hour;
}