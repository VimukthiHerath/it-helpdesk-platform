using Auth.Api.Model;

namespace Auth.Api.DTO;

public class AdminCreateUserResponseDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
