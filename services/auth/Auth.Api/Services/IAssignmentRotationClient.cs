namespace Auth.Api.Services;

public enum RotationCheckResult
{
    NotInRotation,
    InRotation,
    Unreachable,
}

public interface IAssignmentRotationClient
{
    Task<RotationCheckResult> IsUserInRotationAsync(int userId, string? authorizationHeader, CancellationToken cancellationToken);
}
