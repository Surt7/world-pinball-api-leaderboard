namespace WorldPinball.Application.Security.CA;

public sealed record IssuedSoftwareCertificate(
    string CertPem,
    string SerialNumber,
    string PublisherName,
    string ThumbprintSha256,
    string SubjectDn,
    string IssuerDn,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc
);