namespace Assignment.Api.DTO;

public class AssignmentQueueItemDTO
{
    public int TicketId { get; set; }
    public int Urgency { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}
