namespace WorldPinball.Api.Contracts.Certificates;

public sealed record IssueSoftwareCertificateResponse(
    string CertPem,
    string SerialNumber,
    string ThumbprintSha256,
    DateTime NotAfterUtc
);