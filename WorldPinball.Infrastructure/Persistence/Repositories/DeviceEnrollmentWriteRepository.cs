using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Domain.Entities;

namespace WorldPinball.Infrastructure.Persistence.Repositories;

public class DeviceEnrollmentWriteRepository: IDeviceEnrollmentWriteRepository
{
    
    private readonly AppDbContext _db;
    public DeviceEnrollmentWriteRepository(AppDbContext db)
    {
        _db = db;
    }
    
    public async Task SaveEnrollmentAsync(Device device, MachineDevice machineDevice, EnrollmentToken enrollmentToken,
        CancellationToken cancellationToken = default)
    {
        _db.Devices.Add(device);
        _db.MachineDevices.Add(machineDevice);
        _db.EnrollmentTokens.Update(enrollmentToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
    
    
    public async Task AddTokenAsync(
        EnrollmentToken token, 
        CancellationToken cancellationToken = default)
    {
        await _db.EnrollmentTokens.AddAsync(token, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
    
    public async Task UpdateTokenAsync(
        EnrollmentToken token, 
        CancellationToken cancellationToken = default)
    {
        _db.EnrollmentTokens.Update(token);
        await _db.SaveChangesAsync(cancellationToken);
    }
}