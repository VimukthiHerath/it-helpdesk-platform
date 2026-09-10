using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assignment.Api.Model;

// One row per ticket assignment (AC3). The most recent row is also how the
// rotation determines who was assigned last - see RoundRobinAssignmentService.
public class TicketAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    public int AgentId { get; set; }

    [Column("assigned_at")]
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
