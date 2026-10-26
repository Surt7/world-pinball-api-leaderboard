namespace WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;

public abstract record CreateBootstrapTokenResult
{
    public sealed record Success(
        Guid TokenId,
        string RawBootstrapToken,
        DateTime ExpiresAtUtc
    ) : CreateBootstrapTokenResult;
    public sealed record Failure(string Reason) : CreateBootstrapTokenResult;
}