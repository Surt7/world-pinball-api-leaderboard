namespace WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;

public record GetPagedMachinesQuery(
    int PageSize,
    Guid? LastMachineId = null,
    int? PageNumber = null,
    string? Search = null
);
