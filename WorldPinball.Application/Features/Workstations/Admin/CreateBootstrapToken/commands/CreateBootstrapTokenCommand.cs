namespace WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;

public sealed record CreateBootstrapTokenCommand(
    string IssuedByUserId,
    string? Notes = null,
    int ValidityHours = 2
);