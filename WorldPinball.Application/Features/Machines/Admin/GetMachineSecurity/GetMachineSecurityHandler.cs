using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Queries;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;

public sealed class GetMachineSecurityHandler(
    IMachineReadRepository readRepository,
    ILogger<GetMachineSecurityHandler> logger
) : IGetMachineSecurityHandler
{
    private static readonly EventId FetchingSecurity = new(3110, nameof(FetchingSecurity));
    private static readonly EventId MachineNotFoundEvent = new(3111, nameof(MachineNotFoundEvent));

    public async Task<GetMachineSecurityResult> HandleAsync(
        GetMachineSecurityQuery query, 
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(FetchingSecurity, "Fetching security and TPM certificate status for machine {MachineId}", query.MachineId);

        var security = await readRepository.GetMachineSecurityAsync(query.MachineId, cancellationToken);
        if (security is null)
        {
            logger.LogWarning(MachineNotFoundEvent, "Machine with id {MachineId} not found.", query.MachineId);
            return new GetMachineSecurityResult.MachineNotFound(query.MachineId);
        }

        return new GetMachineSecurityResult.Success(security);
    }
}
