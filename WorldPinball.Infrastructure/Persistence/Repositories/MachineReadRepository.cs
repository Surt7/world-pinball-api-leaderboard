using WorldPinball.Application.DTOs;
using System.Collections.Immutable;
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
    
    public async Task<(IReadOnlyList<MachineRowDto> Items, int TotalCount, FleetStatsDto Stats)> GetPagedMachines(
        int pageSize,
        Guid? lastMachineId,
        int? pageNumber,
        string? search,
        CancellationToken ct = default)
    {
        var query = _db.Machines.AsNoTracking();
        
        // Filter by search term across serial, display name, and license
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m =>
                EF.Functions.ILike(m.WorldPinballSerialNumber, $"%{term}%") ||
                EF.Functions.ILike(m.DisplayName, $"%{term}%") ||
                EF.Functions.ILike(m.IpLicense.Name, $"%{term}%"));
        }

        // Count total matching machines for pagination metadata
        var totalCount = await query.CountAsync(ct);

        // Fleet statistics aggregated directly in SQL
        var stats = await _db.Machines.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new FleetStatsDto(
                g.Count(),
                g.Count(m => m.Status == MachineStatus.Active),
                g.Count(m => m.Status == MachineStatus.Inactive || m.Status == MachineStatus.Revoked),
                g.Count(m => m.Status == MachineStatus.Maintenance),
                g.Count(m => m.Status == MachineStatus.PendingEnrollment)
            ))
            .FirstOrDefaultAsync(ct) ?? new FleetStatsDto(0, 0, 0, 0, 0);
        
        // Hybrid pagination:
        // 1. If lastMachineId provided -> Keyset seek (fast sequential navigation)
        // 2. Else if pageNumber provided -> Offset skip (direct jump to arbitrary page)
        if (lastMachineId is { } lastId)
        {
            query = query.Where(m => m.MachineId.CompareTo(lastId) > 0);
        }
        else if (pageNumber is > 1)
        {
            query = query.Skip((pageNumber.Value - 1) * pageSize);
        }
        
        // Fetch pageSize + 1 to detect if a next page exists without full scan
        var rawItems = await (from machine in query.OrderBy(m => m.MachineId).Take(pageSize + 1)
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

        return (rawItems, totalCount, stats);
    }
    
}
