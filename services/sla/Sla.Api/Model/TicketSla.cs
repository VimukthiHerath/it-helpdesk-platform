using System.ComponentModel.DataAnnotations;

namespace Sla.Api.Model;

public class TicketSla
{
    [Key]
    public int TicketId { get; set; }
    
    public DateTime TargetResolutionTimeUtc { get; set; }
}
