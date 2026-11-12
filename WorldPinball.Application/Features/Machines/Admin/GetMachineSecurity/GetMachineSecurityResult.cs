using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;

namespace WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;

public abstract record GetMachineSecurityResult
{
    private GetMachineSecurityResult() { }

    public sealed record Success(MachineSecurityDto Security) : GetMachineSecurityResult;
    public sealed record MachineNotFound(Guid MachineId) : GetMachineSecurityResult;
}
