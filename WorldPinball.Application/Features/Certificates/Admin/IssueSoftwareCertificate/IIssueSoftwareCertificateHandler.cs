using WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate.commands;

namespace WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate;

public interface IIssueSoftwareCertificateHandler
{
    Task<IssueSoftwareCertificateResult> HandleAsync(
        IssueSoftwareCertificateCommand command,
        CancellationToken ct = default);
}