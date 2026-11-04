using WorldPinball.Application.Features.Leaderboard.Public.Queries;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Interfaces.Services;

namespace WorldPinball.Application.Features.Leaderboard.Public;

public sealed class GetLeaderboardHandler(
    ILeaderboardReadRepository repository,
    IRealtimeLeaderboardService realTimeService
):IGetLeaderboardHandler
{
public async Task<GetLeaderboardResult> HandleAsync(
        GetLeaderboardQuery query,
        CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var ttl = TimeSpan.FromMinutes(1);
        var hasGuids = query.IpId.HasValue && query.ModeId.HasValue;
        var hasSlugs = !string.IsNullOrWhiteSpace(query.IpSlug) 
                       && !string.IsNullOrWhiteSpace(query.ModeCode);
        
        if (!hasGuids && !hasSlugs)
        {
            return new GetLeaderboardResult.InvalidParameters(
                "Either (IpId and ModeId) or (IpSlug and ModeCode) must be provided.");
        }
        
        if (query is { IpId: { } ipId, ModeId: { } modeId })
        {
            var cached = await realTimeService.GetCachedLeaderboardAsync(
                ipId, modeId, limit, ct);
            if (cached is not null)
            {
                return new GetLeaderboardResult.Success(cached);
            }
        }
        
        var dbResults = await repository.GetLeaderboardAsync(
            query.IpId, query.ModeId, query.IpSlug, query.ModeCode, limit, 0, ct);
        if (query is { IpId: { } ip, ModeId: { } mode })
        {
            await realTimeService.SetCachedLeaderboardAsync(
                ip, mode, limit, dbResults, ttl, ct);
        }

        return new GetLeaderboardResult.Success(dbResults);
    }
}
