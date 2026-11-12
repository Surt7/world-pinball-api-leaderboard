namespace WorldPinball.Api.Contracts.Workstations;

public sealed record EnrollWorkstationRequest(
    string PlainBootstrapToken,
    string WorkstationName,
    string PublicKeyPem
);