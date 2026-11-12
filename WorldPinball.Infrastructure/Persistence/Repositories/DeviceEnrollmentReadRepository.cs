using Microsoft.EntityFrameworkCore;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Infrastructure.Persistence.Repositories;

public class DeviceEnrollmentReadRepository : IDeviceEnrollmentReadRepository
{
    private readonly AppDbContext _db;

    public DeviceEnrollmentReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<EnrollmentToken?> GetTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return await _db.EnrollmentTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<bool> DeviceSerialExistsAsync(
        string serialPhysical, 
        CancellationToken cancellationToken = default)
    {
        return await _db.Devices
            .AnyAsync(d => d.SerialPhysical == serialPhysical, cancellationToken);
    }

    public async Task<CaCertificate?> GetActiveCaAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.CaCertificates
            .Where(c => c.IsActive && c.IsTrusted && c.CaType == CaType.Intermediate)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.CaCertificates
                .Where(c => c.IsActive && c.IsTrusted)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
    }
}
