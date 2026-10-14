using System.ComponentModel.DataAnnotations;

namespace Auth.Api.DTO
{
    public class UserLoginDTO
    {
        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;
        [Required]
        [MaxLength(255)]
        public string Password { get; set; } = string.Empty;
    }
}