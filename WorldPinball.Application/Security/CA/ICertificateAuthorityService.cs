using WorldPinball.Domain.Enums;

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
        WorkstationType workstationType = WorkstationType.AdminWorkstation,
        CancellationToken ct = default);
}
