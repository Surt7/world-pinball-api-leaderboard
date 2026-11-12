namespace WorldPinball.Api.Contracts.Certificates;

public sealed record IssueSoftwareCertificateRequest(
    string PublisherName,
    string PublicKeyPem
);