using System.ComponentModel.DataAnnotations;

namespace Ticket.Api.Model;

public class Ticket
{
    [Key]
    public int Id { get; set; }
    
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? AssignedAgentId { get; set; }
    public DateTime? SlaDeadlineUtc { get; set; }
}
