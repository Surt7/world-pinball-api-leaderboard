using WorldPinball.Domain.Enums;

namespace WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;

public sealed record MachineRowDto(
    Guid MachineId,
    string MachineName,
    string DisplayName,
    MachineStatus MachineStatus,
    string? MachineNotes,
    DateTime RegistredAtUtc,
    DateTime? LastUpdated,
    string SerialNumber,
    DateTime? CertExpiresAtUtc = null,
    int? CertStatus = null);
