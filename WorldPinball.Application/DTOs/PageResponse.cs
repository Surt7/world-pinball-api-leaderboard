namespace WorldPinball.Application.DTOs;

public record PagedResponse<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int PageNumber { get; init; } = 1;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }
    public Guid? LastId { get; init; }
    public FleetStatsDto? Stats { get; init; }
}
