namespace Auth.Api.DTO;

using System.ComponentModel.DataAnnotations;

public class ForgotPasswordRequestDTO
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
