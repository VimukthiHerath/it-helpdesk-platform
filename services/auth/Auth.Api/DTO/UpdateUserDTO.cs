using System.ComponentModel.DataAnnotations;
using Auth.Api.Model;

namespace Auth.Api.DTO;

public class UpdateUserDTO
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression(PhoneNumber.Pattern, ErrorMessage = PhoneNumber.ErrorMessage)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; }
}
