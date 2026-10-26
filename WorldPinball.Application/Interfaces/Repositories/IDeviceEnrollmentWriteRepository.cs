using WorldPinball.Domain.Entities;

namespace WorldPinball.Application.Interfaces.Repositories;



public interface IDeviceEnrollmentWriteRepository
{
    Task SaveEnrollmentAsync(
        Device device,
        MachineDevice machineDevice,
        EnrollmentToken enrollmentToken,
        CancellationToken cancellationToken = default);
    
    Task AddTokenAsync(
        EnrollmentToken token,
        CancellationToken cancellationToken = default);
    
    Task UpdateTokenAsync(
        EnrollmentToken token,
        CancellationToken cancellationToken = default);
}

