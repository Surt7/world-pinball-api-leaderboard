namespace WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation.commands;

public sealed record EnrollWorkstationCommand(
    string PlainBootstrapToken,
    string WorkstationName,
    string PublicKeyPem
);