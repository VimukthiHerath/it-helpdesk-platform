using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ticket.Api.Model;

// AC3: one row per status change, kept forever - Tickets.Status only ever
// holds the current value, this table is the actual history.
public class TicketStatusHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    public TicketStatus OldStatus { get; set; }

    [Required]
    public TicketStatus NewStatus { get; set; }

    // Auth user id of whoever made the change (the agent or administrator),
    // same plain-int-no-FK convention as Tickets.CreatedBy/AssignedTo.
    [Required]
    [Column("changed_by")]
    public int ChangedBy { get; set; }

    [Column("changed_at")]
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
