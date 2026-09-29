namespace Notification.Api.Services;

public sealed class AuthApiUserEmailResolver : IUserEmailResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthApiUserEmailResolver> _logger;

    public AuthApiUserEmailResolver(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AuthApiUserEmailResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> ResolveUserEmailAsync(int userId, CancellationToken cancellationToken)
    {
        var authApiUrl = _configuration["AuthApiUrl"] ?? "http://localhost:5121";
        var client = _httpClientFactory.CreateClient();

        try
        {
            var response = await client.GetFromJsonAsync<AuthUserEmailDto>(
                $"{authApiUrl}/api/auth/internal/users/{userId}/email", cancellationToken);
            return response?.Email;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve email for user {UserId} from Auth.Api.", userId);
            return null;
        }
    }

    private sealed record AuthUserEmailDto(string Email, string Name);
}
