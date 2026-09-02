using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ticket.Api.Model;

public class Tickets
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string IssueType { get; set; } = string.Empty;

    [Required]
    public TicketUrgency Urgency { get; set; } = TicketUrgency.one_hour;

    public TicketStatus Status { get; set; } = TicketStatus.unassigned;

    public int? AssignedTo { get; set; }
    
    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}