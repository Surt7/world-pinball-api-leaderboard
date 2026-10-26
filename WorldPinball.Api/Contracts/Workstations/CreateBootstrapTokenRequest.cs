namespace WorldPinball.Api.Contracts.Workstations;

public sealed record CreateBootstrapTokenRequest(
    string? Notes = null,
    int ValidityHours = 2
);