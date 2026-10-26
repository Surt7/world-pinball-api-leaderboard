namespace WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;

public sealed record MachineSecurityDto(
    Guid MachineId,
    string SerialNumber,
    string DisplayName,
    int MachineStatus,
    bool HasAttachedDevice,
    Guid? DeviceId,
    string? DevicePhysicalSerial,
    bool HasCertificate,
    string? CertSerialNumber,
    string? CertThumbprintSha256,
    string? SubjectDn,
    string? IssuerDn,
    DateTime? NotBeforeUtc,
    DateTime? NotAfterUtc,
    int? CertStatus,
    bool IsExpired,
    bool IsValid,
    string StatusExplanation
);
