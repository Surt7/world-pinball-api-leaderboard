using WorldPinball.Domain.Enums;

namespace WorldPinball.Api.Contracts.Workstations;

public sealed record CreateBootstrapTokenResponse(
    Guid TokenId,
    string RawBootstrapToken,
    DateTime ExpiresAtUtc,
    WorkstationType WorkstationType
);