using Microsoft.EntityFrameworkCore;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Infrastructure.Persistence.Repositories;

public sealed class MachineReadRepository : IMachineReadRepository
{
    private readonly AppDbContext _db;

    public MachineReadRepository(AppDbContext db)
    {
        _db = db;
    }
    
    public async Task<IReadOnlyList<MachineRowDto>> GetAllMachines(CancellationToken ct)
    {
        return await (from machine in _db.Machines.AsNoTracking()
                      let activeCert = _db.MachineDevices
                          .Where(md => md.MachineId == machine.MachineId && md.IsActive)
                          .SelectMany(md => _db.DeviceCertificates
                              .Where(c => c.DeviceId == md.DeviceId)
                              .OrderByDescending(c => c.IssuedAtUtc))
                          .FirstOrDefault()
                      select new MachineRowDto(
                          machine.MachineId,
                          machine.IpLicense.Name,
                          machine.DisplayName,
                          machine.Status,
                          machine.MachineNotes,
                          machine.RegisteredAtUtc,
                          machine.UpdatedAtUtc,
                          machine.WorldPinballSerialNumber,
                          activeCert != null ? activeCert.NotAfterUtc : (DateTime?)null,
                          activeCert != null ? (int)activeCert.Status : (int?)null
                      )).ToListAsync(ct);
    }
    
    public async Task<MachineRowDto?> GetMachinesWithId(Guid machineId, CancellationToken ct)
    {
        return await (from machine in _db.Machines.AsNoTracking()
                      where machine.MachineId == machineId
                      let activeCert = _db.MachineDevices
                          .Where(md => md.MachineId == machine.MachineId && md.IsActive)
                          .SelectMany(md => _db.DeviceCertificates
                              .Where(c => c.DeviceId == md.DeviceId)
                              .OrderByDescending(c => c.IssuedAtUtc))
                          .FirstOrDefault()
                      select new MachineRowDto(
                          machine.MachineId,
                          machine.IpLicense.Name,
                          machine.DisplayName,
                          machine.Status,
                          machine.MachineNotes,
                          machine.RegisteredAtUtc,
                          machine.UpdatedAtUtc,
                          machine.WorldPinballSerialNumber,
                          activeCert != null ? activeCert.NotAfterUtc : (DateTime?)null,
                          activeCert != null ? (int)activeCert.Status : (int?)null
                      )).SingleOrDefaultAsync(ct);
    }

    public async Task<MachineSecurityDto?> GetMachineSecurityAsync(Guid machineId, CancellationToken ct = default)
    {
        var machine = await _db.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MachineId == machineId, ct);

        if (machine is null)
            return null;

        var activeDeviceLink = await _db.MachineDevices
            .Include(md => md.Device)
                .ThenInclude(d => d.Certificates)
            .AsNoTracking()
            .FirstOrDefaultAsync(md => md.MachineId == machineId && md.IsActive, ct);

        if (activeDeviceLink is null)
        {
            return new MachineSecurityDto(
                machine.MachineId,
                machine.WorldPinballSerialNumber,
                machine.DisplayName,
                (int)machine.Status,
                HasAttachedDevice: false,
                DeviceId: null,
                DevicePhysicalSerial: null,
                HasCertificate: false,
                CertSerialNumber: null,
                CertThumbprintSha256: null,
                SubjectDn: null,
                IssuerDn: null,
                NotBeforeUtc: null,
                NotAfterUtc: null,
                CertStatus: null,
                IsExpired: false,
                IsValid: false,
                StatusExplanation: "Aucun périphérique physique (TPM) n'est actuellement rattaché à ce flipper."
            );
        }

        var device = activeDeviceLink.Device;
        var activeCert = device.Certificates
            .OrderByDescending(c => c.IssuedAtUtc)
            .FirstOrDefault();

        if (activeCert is null)
        {
            return new MachineSecurityDto(
                machine.MachineId,
                machine.WorldPinballSerialNumber,
                machine.DisplayName,
                (int)machine.Status,
                HasAttachedDevice: true,
                DeviceId: device.DeviceId,
                DevicePhysicalSerial: device.SerialPhysical,
                HasCertificate: false,
                CertSerialNumber: null,
                CertThumbprintSha256: null,
                SubjectDn: null,
                IssuerDn: null,
                NotBeforeUtc: null,
                NotAfterUtc: null,
                CertStatus: null,
                IsExpired: false,
                IsValid: false,
                StatusExplanation: "Périphérique TPM détecté mais aucun certificat n'a été émis. Enrôlement requis."
            );
        }

        var now = DateTime.UtcNow;
        bool isExpired = now > activeCert.NotAfterUtc;
        bool isRevoked = activeCert.Status == DeviceCertificateStatus.Revoked;
        bool isValid = activeCert.Status == DeviceCertificateStatus.Active && !isExpired && now >= activeCert.NotBeforeUtc;

        string explanation = isValid
            ? "Certificat matériel TPM valide et vérifié. Liaison mTLS opérationnelle."
            : isRevoked
                ? $"Certificat révoqué le {activeCert.RevokedAtUtc:dd/MM/yyyy} : {activeCert.RevokeReason}"
                : isExpired
                    ? $"Certificat matériel expiré depuis le {activeCert.NotAfterUtc:dd/MM/yyyy}."
                    : "Certificat non actif.";

        return new MachineSecurityDto(
            machine.MachineId,
            machine.WorldPinballSerialNumber,
            machine.DisplayName,
            (int)machine.Status,
            HasAttachedDevice: true,
            DeviceId: device.DeviceId,
            DevicePhysicalSerial: device.SerialPhysical,
            HasCertificate: true,
            CertSerialNumber: activeCert.CertSerialNumber,
            CertThumbprintSha256: activeCert.CertThumbprintSha256,
            SubjectDn: activeCert.SubjectDn,
            IssuerDn: activeCert.IssuerDn,
            NotBeforeUtc: activeCert.NotBeforeUtc,
            NotAfterUtc: activeCert.NotAfterUtc,
            CertStatus: (int)activeCert.Status,
            IsExpired: isExpired,
            IsValid: isValid,
            StatusExplanation: explanation
        );
    }
}
