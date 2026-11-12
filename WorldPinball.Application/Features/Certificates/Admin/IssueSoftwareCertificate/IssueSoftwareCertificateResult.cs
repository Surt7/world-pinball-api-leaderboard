namespace WorldPinball.Application.Features.Certificates.Admin.IssueSoftwareCertificate;

public abstract record IssueSoftwareCertificateResult
{
    public sealed record Success(
        string CertPem,
        string SerialNumber,
        string ThumbprintSha256,
        DateTime NotAfterUtc
    ) : IssueSoftwareCertificateResult;

    public sealed record InvalidPublicKey(string Reason) : IssueSoftwareCertificateResult;
}