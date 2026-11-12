namespace WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate.commands;

public sealed record IssueSoftwareCertificateCommand(
    string PublisherName,
    string PublicKeyPem
);