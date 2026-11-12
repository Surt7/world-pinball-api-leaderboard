namespace WorldPinball.Application.Security.CA;

public sealed record IssuedDeviceCertificate(
    string CertPem,
    string SerialNumber,
    Guid MachineId,
    string ThumbprintSha256,
    string SubjectDn,
    string IssuerDn,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc
);