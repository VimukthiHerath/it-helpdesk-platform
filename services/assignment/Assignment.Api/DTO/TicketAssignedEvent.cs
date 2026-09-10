namespace Assignment.Api.DTO;

public class TicketAssignedEvent
{
    public string EventType { get; set; } = "TicketAssigned";
    public int TicketId { get; set; }
    public int AgentUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}
