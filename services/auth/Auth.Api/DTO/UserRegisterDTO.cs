using System.ComponentModel.DataAnnotations;
using Auth.Api.Model;

namespace Auth.Api.DTO;

public class UserRegisterDTO
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Employee;
}
