using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;

namespace WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;

public interface IGetPagedMachinesHandler
{
    Task<GetPagedMachinesResult> HandleAsync(
        GetPagedMachinesQuery query, 
        CancellationToken ct = default);
}