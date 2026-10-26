namespace WorldPinball.Application.Security.CA;

public sealed record IssuedWorkstationCertificate(
    string CertPem,
    string SerialNumber,
    string WorkstationName,
    string ThumbprintSha256,
    string SubjectDn,
    string IssuerDn,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc
);