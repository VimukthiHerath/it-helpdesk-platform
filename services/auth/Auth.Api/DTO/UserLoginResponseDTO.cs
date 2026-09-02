namespace Auth.Api.DTO
{
    public class UserLoginResponseDTO
    {
        public string Token { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}