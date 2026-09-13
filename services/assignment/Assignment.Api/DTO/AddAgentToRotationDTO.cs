using System.ComponentModel.DataAnnotations;

namespace Assignment.Api.DTO;

public class AddAgentToRotationDTO
{
    [Required]
    public int UserId { get; set; }
}
