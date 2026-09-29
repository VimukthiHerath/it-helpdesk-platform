namespace Notification.Api.Services;

public sealed class AuthApiAdminEmailResolver : IAdminEmailResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthApiAdminEmailResolver> _logger;

    public AuthApiAdminEmailResolver(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AuthApiAdminEmailResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<string>> ResolveAdminEmailsAsync(CancellationToken cancellationToken)
    {
        var authApiUrl = _configuration["AuthApiUrl"] ?? "http://localhost:5121";
        var client = _httpClientFactory.CreateClient();

        try
        {
            var response = await client.GetFromJsonAsync<AdminEmailsDto>(
                $"{authApiUrl}/api/auth/internal/admins/emails", cancellationToken);
            return response?.Emails ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve administrator emails from Auth.Api.");
            return new List<string>();
        }
    }

    private sealed record AdminEmailsDto(List<string> Emails);
}
