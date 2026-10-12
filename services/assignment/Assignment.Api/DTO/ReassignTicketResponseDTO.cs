namespace Assignment.Api.DTO;

public class ReassignTicketResponseDTO
{
    public int TicketId { get; set; }
    public int AgentUserId { get; set; }
    public int Urgency { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}
