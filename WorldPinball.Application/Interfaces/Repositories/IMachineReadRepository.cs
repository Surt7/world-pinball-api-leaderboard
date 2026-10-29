using WorldPinball.Application.DTOs;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;

namespace WorldPinball.Application.Interfaces.Repositories;

public interface IMachineReadRepository
{
    Task<IReadOnlyList<MachineRowDto>> GetAllMachines(
        CancellationToken ct = default);

    Task<MachineRowDto?> GetMachinesWithId(
        Guid machineId,
        CancellationToken ct = default);

    Task<MachineSecurityDto?> GetMachineSecurityAsync(
        Guid machineId,
        CancellationToken ct = default);
    
    Task<(IReadOnlyList<MachineRowDto> Items, int TotalCount, FleetStatsDto Stats)> GetPagedMachines(
        int pageSize,
        Guid? lastMachineId,
        int? pageNumber,
        string? search,
        CancellationToken ct = default);
}
