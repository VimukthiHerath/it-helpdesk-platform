using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assignment.Api.Model;

// The fixed rotation list. UserId is the agent's id in Auth's Users table,
// but there is no cross-service foreign key - Assignment does not call Auth.
public class Agent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    // Fixed rotation order (AC1). Unique - two agents can't share a slot.
    [Required]
    [Column("display_order")]
    public int DisplayOrder { get; set; }
}
