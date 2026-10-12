using System.ComponentModel.DataAnnotations;

namespace Sla.Api.Models;

public class SlaTierPolicy
{
    [Key]
    public string TierName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByAdminId { get; set; }
}
