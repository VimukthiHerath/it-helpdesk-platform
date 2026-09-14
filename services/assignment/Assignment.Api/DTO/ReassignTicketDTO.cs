using System.ComponentModel.DataAnnotations;

namespace Assignment.Api.DTO;

public class ReassignTicketDTO
{
    [Required]
    public int NewAgentUserId { get; set; }
}
