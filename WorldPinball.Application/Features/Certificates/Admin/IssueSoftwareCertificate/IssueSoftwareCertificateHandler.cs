using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate.commands;
using WorldPinball.Application.Security.CA;

namespace WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate;

public class IssueSoftwareCertificateHandler(
    ICertificateAuthorityService caService,
    ILogger<IssueSoftwareCertificateHandler> logger
) : IIssueSoftwareCertificateHandler
    
{
    
    private static readonly EventId SoftwareCertificateIssued =
        new(2160, nameof(SoftwareCertificateIssued));

    private static readonly EventId SoftwareCertificateIssueFailed =
        new(2161, nameof(SoftwareCertificateIssueFailed));


    
    public async Task<IssueSoftwareCertificateResult> HandleAsync(
        IssueSoftwareCertificateCommand command, 
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.PublisherName))
        {
            return new IssueSoftwareCertificateResult.InvalidPublicKey("Publisher name is required.");
        }
        if (string.IsNullOrWhiteSpace(command.PublicKeyPem))
        {
            return new IssueSoftwareCertificateResult.InvalidPublicKey("Public key PEM is required.");
        }
        
        try
        {
            var issued = await caService.IssueSoftwareSigningCertificateAsync(
                command.PublisherName,
                command.PublicKeyPem,
                ct);
            logger.LogInformation(
                SoftwareCertificateIssued,
                "Issued code signing certificate for publisher {PublisherName} (Serial: {CertSerial})",
                command.PublisherName,
                issued.SerialNumber);
            return new IssueSoftwareCertificateResult.Success(
                issued.CertPem,
                issued.SerialNumber,
                issued.ThumbprintSha256,
                issued.NotAfterUtc);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(
                SoftwareCertificateIssueFailed,
                ex,
                "Failed to issue software certificate for publisher {PublisherName}: invalid key format",
                command.PublisherName);
            return new IssueSoftwareCertificateResult.InvalidPublicKey(ex.Message);
        }
    }
}