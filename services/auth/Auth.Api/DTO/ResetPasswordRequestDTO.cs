namespace Auth.Api.DTO;

using System.ComponentModel.DataAnnotations;

public class ResetPasswordRequestDTO
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", ErrorMessage = "Password must be at least 8 characters, contain one uppercase, one lowercase, one number, and one special character.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare("NewPassword", ErrorMessage = "New password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
