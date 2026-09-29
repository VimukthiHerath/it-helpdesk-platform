namespace Notification.Api.Models;

/// <summary>
/// Tracks Kafka events that have already been processed to prevent
/// duplicate emails from Kafka's at-least-once delivery guarantee (SCRUM-28 AC2).
/// </summary>
public class ProcessedEvent
{
    public int Id { get; set; }

    /// <summary>
    /// Unique key composed of "EventType-Partition-Offset" e.g. "TicketCreated-0-42"
    /// </summary>
    public string EventKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
