namespace WorldPinball.Api.Contracts.Workstations;

public sealed record EnrollWorkstationResponse(
    string CertPem,
    string SerialNumber,
    string ThumbprintSha256,
    DateTime NotAfterUtc
);