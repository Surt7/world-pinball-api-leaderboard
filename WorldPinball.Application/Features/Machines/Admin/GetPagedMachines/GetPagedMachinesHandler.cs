using WorldPinball.Application.DTOs;
using WorldPinball.Application.Features.Machines.Admin.GetPagedMachines.Queries;
using WorldPinball.Application.Features.Machines.Admin.Provision.Dtos;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Application.Features.Machines.Admin.GetPagedMachines;

public class GetPagedMachinesHandler(
    IMachineReadRepository readRepository
    ) : IGetPagedMachinesHandler
{
    public async Task<GetPagedMachinesResult> HandleAsync(
        GetPagedMachinesQuery query, 
        CancellationToken ct = default)
    {
        // 1. Fetch data with pageSize + 1, total count, and aggregated fleet stats
        var (rawItems, totalCount, stats) = await readRepository.GetPagedMachines(
            query.PageSize,
            query.LastMachineId,
            query.PageNumber,
            query.Search,
            ct);

        var items = rawItems.ToList();
        
        // 2. Return NotFound ONLY if the entire fleet in database is empty
        // If a search query yields 0 results, we return 200 OK with empty items!
        if (stats.Total == 0)
        {
            return new GetPagedMachinesResult.NotFound();
        }
        
        // 3. Check if an extra item was returned
        var hasNextPage = items.Count > query.PageSize;
        if (hasNextPage)
        {
            items.RemoveAt(query.PageSize); // Remove the extra probe item
        }
        
        // 4. Build page response metadata
        var pageNumber = query.PageNumber ?? 1;
        var hasPreviousPage = query.LastMachineId.HasValue || pageNumber > 1;

        var page = new PagedResponse<MachineRowDto>
        {
            Items = items,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
            LastId = items.LastOrDefault()?.MachineId,
            Stats = stats
        };
        
        return new GetPagedMachinesResult.Success(page);
    }
}
