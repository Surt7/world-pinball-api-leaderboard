namespace WorldPinball.Application.Security.CA;

public interface ICertificateAuthorityService
{
    Task<IssuedDeviceCertificate> IssueDeviceCertificateAsync(
        Guid machineId,
        string deviceSerialNumber,
        string publicKeyPem,
        CancellationToken ct = default);
    
    Task<IssuedSoftwareCertificate> IssueSoftwareSigningCertificateAsync(
        string publisherName,
        string publicKeyPem,
        CancellationToken ct = default);
    
    Task<IssuedWorkstationCertificate> IssueWorkstationCertificateAsync(
        string workstationName,
        string publicKeyPem,
        CancellationToken ct = default);
}
