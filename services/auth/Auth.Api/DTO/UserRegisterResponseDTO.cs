using Auth.Api.Model;

namespace Auth.Api.DTO
{
    public class UserRegisterResponseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Employee;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}