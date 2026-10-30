namespace WorldPinball.Bff.Services;

/// <summary>
/// Result contracts for player user registration through Keycloak.
/// </summary>
public abstract record RegisterUserResult
{
    public sealed record Success(string UserId) : RegisterUserResult;
    public sealed record UsernameConflict(string Message) : RegisterUserResult;
    public sealed record EmailConflict(string Message) : RegisterUserResult;
    public sealed record ValidationError(string Message) : RegisterUserResult;
    public sealed record Failure(string Message) : RegisterUserResult;
}

/// <summary>
/// Service interface responsible for managing user accounts in Keycloak.
/// </summary>
public interface IKeycloakAdminService
{
    Task<RegisterUserResult> RegisterUserAsync(
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default
    );
}
