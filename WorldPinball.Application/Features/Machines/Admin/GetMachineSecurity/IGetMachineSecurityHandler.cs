using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Queries;

namespace WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;

public interface IGetMachineSecurityHandler
{
    Task<GetMachineSecurityResult> HandleAsync(
        GetMachineSecurityQuery query, 
        CancellationToken cancellationToken = default);
}
