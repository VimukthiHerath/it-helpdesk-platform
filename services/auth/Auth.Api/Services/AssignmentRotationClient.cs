using System.Net.Http.Json;

namespace Auth.Api.Services;

// Auth.Api otherwise never calls another service - every other cross-service
// concern in this codebase is handled by trusting the JWT or by Kafka events.
// This is a deliberate, single exception: whether a user is still in
// Assignment's round-robin rotation is business state Auth has no other way
// to know, and getting it wrong would let an agent's account be deactivated
// or re-roled while tickets keep landing on them. See
// agent-rotation-management.md for the full reasoning.
//
// The caller's own bearer token is forwarded as-is rather than minting a
// separate service token - the caller already passed Auth's own
// [Authorize(Roles = Administrator)] check, so the same token satisfies
// Assignment's identical role check on GET /api/assignments/agents.
public class AssignmentRotationClient : IAssignmentRotationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AssignmentRotationClient> _logger;

    public AssignmentRotationClient(IHttpClientFactory httpClientFactory, ILogger<AssignmentRotationClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("AssignmentApi");
        _logger = logger;
    }

    public async Task<RotationCheckResult> IsUserInRotationAsync(int userId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        try
        {
            // First attempt to query the dedicated rotation status endpoint
            using var directRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/Assignment/Rotation/{userId}");
            if (!string.IsNullOrEmpty(authorizationHeader))
            {
                directRequest.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
            }

            var directResponse = await _httpClient.SendAsync(directRequest, cancellationToken);
            if (directResponse.IsSuccessStatusCode)
            {
                var rosterRecord = await directResponse.Content.ReadFromJsonAsync<AgentRotationEntry>(cancellationToken: cancellationToken);
                if (rosterRecord != null && rosterRecord.IsActive)
                {
                    return RotationCheckResult.InRotation;
                }
                return RotationCheckResult.NotInRotation;
            }

            if (directResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return RotationCheckResult.NotInRotation;
            }

            // Fallback to checking /api/assignments/agents list
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/assignments/agents");
            if (!string.IsNullOrEmpty(authorizationHeader))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Assignment.Api rotation check returned {StatusCode} for user {UserId}",
                    response.StatusCode, userId);
                return RotationCheckResult.Unreachable;
            }

            var agents = await response.Content.ReadFromJsonAsync<List<AgentRotationEntry>>(cancellationToken: cancellationToken)
                ?? new List<AgentRotationEntry>();

            // Only consider the agent in rotation if they exist AND are active
            return agents.Any(a => a.UserId == userId && a.IsActive)
                ? RotationCheckResult.InRotation
                : RotationCheckResult.NotInRotation;
        }
        catch (Exception ex)
        {
            // Fail closed: if we can't reach Assignment.Api, we don't know
            // whether it's safe to change this account, so we don't allow it.
            _logger.LogError(ex, "Unable to reach Assignment.Api to check rotation status for user {UserId}", userId);
            return RotationCheckResult.Unreachable;
        }
    }

    private sealed class AgentRotationEntry
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
