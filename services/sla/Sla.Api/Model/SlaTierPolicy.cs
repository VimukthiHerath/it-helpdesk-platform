using System.ComponentModel.DataAnnotations;

namespace Sla.Api.Model;

public class SlaTierPolicy
{
    [Key]
    public string TierName { get; set; } = string.Empty; // e.g., "LOW", "MEDIUM", "HIGH", "CRITICAL"
    
    public int DurationMinutes { get; set; }
    
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    public string? UpdatedByAdminId { get; set; }
}
