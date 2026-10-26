using WorldPinball.Domain.Enums;

namespace WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;

public sealed record CreateBootstrapTokenCommand(
    string IssuedByUserId,
    WorkstationType WorkstationType = WorkstationType.FieldTerminal,
    string? Notes = null,
    int ValidityHours = 2
);