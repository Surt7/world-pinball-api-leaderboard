using WorldPinball.Application.DTOs;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;

namespace WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;

public abstract record GetPagedMachinesResult
{
    private GetPagedMachinesResult() { }

    public sealed record Success(PagedResponse<MachineRowDto> Page) : GetPagedMachinesResult;
    public sealed record NotFound : GetPagedMachinesResult;
}