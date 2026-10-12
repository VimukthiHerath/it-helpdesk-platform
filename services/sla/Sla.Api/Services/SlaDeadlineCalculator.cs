namespace Sla.Api.Services;

// SLA-2 AC1: maps a ticket's urgency tier to its SLA duration. Extracted as a
// pure, static function - no database or Kafka needed - so it's directly
// unit-testable.
public static class SlaDeadlineCalculator
{
    public static TimeSpan DurationFor(int urgency) => urgency switch
    {
        0 => TimeSpan.FromHours(1),
        1 => TimeSpan.FromHours(6),
        2 => TimeSpan.FromHours(12),
        3 => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(24), // Default fallback
    };

    public static DateTime ComputeDeadline(DateTime createdAtUtc, int urgency) =>
        createdAtUtc.Add(DurationFor(urgency));
}
