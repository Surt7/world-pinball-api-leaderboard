using WorldPinball.Domain.Entities;

namespace WorldPinball.Application.Interfaces.Repositories;

public interface IDeviceEnrollmentReadRepository
{
    Task<EnrollmentToken?> GetTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task<bool> DeviceSerialExistsAsync(
        string serialPhysical,
        CancellationToken cancellationToken = default);

    Task<CaCertificate?> GetActiveCaAsync(
        CancellationToken cancellationToken = default);
}
