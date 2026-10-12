namespace Ticket.Api.Events;

public record TicketResolvedEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public int TicketId { get; init; }
    public DateTime ResolvedAtUtc { get; init; } = DateTime.UtcNow;
    public string? ResolvedByUserId { get; init; }
    public string Status { get; init; } = "Resolved";
}
