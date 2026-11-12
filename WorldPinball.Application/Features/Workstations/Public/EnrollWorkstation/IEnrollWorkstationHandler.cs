using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation.commands;

namespace WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;

public interface IEnrollWorkstationHandler
{
    Task<EnrollWorkstationResult> HandleAsync(
        EnrollWorkstationCommand command,
        CancellationToken ct = default);
}