using WorldPinball.Domain.Enums;

namespace WorldPinball.Api.Contracts.Workstations;

public sealed record CreateBootstrapTokenRequest(
    WorkstationType WorkstationType = WorkstationType.FieldTerminal,
    string? Notes = null,
    int ValidityHours = 2
);