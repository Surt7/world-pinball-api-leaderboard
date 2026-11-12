using WorldPinball.Application.Features.Leaderboard.Public.Dtos;

namespace WorldPinball.Application.Interfaces.Repositories
{
    public interface ILeaderboardReadRepository
    {
        Task<IReadOnlyList<LeaderboardRowDto>> GetLeaderboardAsync(
            Guid? ipId = null,
            Guid? modeId = null,
            string? ipSlug = null,
            string? modeCode = null,
            int limit = 50,
            int skip = 0,
            CancellationToken cancellationToken = default);
    }
}
