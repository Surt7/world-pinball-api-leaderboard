using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using WorldPinball.Application.Features.Leaderboard.Public.Dtos;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Infrastructure.Persistence.Repositories;

public sealed class LeaderboardReadRepository(
    IConfiguration config,
    ILogger<LeaderboardReadRepository> logger
) : ILeaderboardReadRepository
{
    private const string Sql = """
        WITH RankedScores AS (
            SELECT
                se.score_entry_id AS ScoreEntryId,
                COALESCE(p.initials, 'AAA') AS PlayerInitials,
                COALESCE(p.display_name, 'Anonymous') AS PlayerName,
                se.score AS Score,
                se.achieved_at_utc AS AchievedAtUtc,
                ip.name AS IpName,
                gm.name AS ModeName,
                CAST(DENSE_RANK() OVER (ORDER BY se.score DESC, se.achieved_at_utc ASC) AS integer) AS Rank,
                CAST(LAG(se.score) OVER (ORDER BY se.score DESC, se.achieved_at_utc ASC) - se.score AS bigint) AS PointsBehindPrevious,
                CAST(FIRST_VALUE(se.score) OVER (ORDER BY se.score DESC, se.achieved_at_utc ASC) - se.score AS bigint) AS PointsBehindLeader
            FROM public.score_entries se
            INNER JOIN public.ip_licenses ip ON ip.ip_id = se.ip_id
            INNER JOIN public.game_modes gm ON gm.mode_id = se.mode_id
            LEFT JOIN public.user_profiles p ON p.user_id::text = se.user_id
            WHERE se.is_validated = true
              AND (@IpId IS NULL OR se.ip_id = @IpId)
              AND (@ModeId IS NULL OR se.mode_id = @ModeId)
              AND (@IpSlug IS NULL OR ip.slug = @IpSlug)
              AND (@ModeCode IS NULL OR gm.code = @ModeCode)
        )
        SELECT
            Rank,
            ScoreEntryId,
            PlayerInitials,
            PlayerName,
            Score,
            PointsBehindPrevious,
            PointsBehindLeader,
            AchievedAtUtc,
            IpName,
            ModeName
        FROM RankedScores
        ORDER BY Rank ASC, AchievedAtUtc ASC
        OFFSET @Skip
        LIMIT @Limit;
        """;

    public async Task<IReadOnlyList<LeaderboardRowDto>> GetLeaderboardAsync(
        Guid? ipId = null,
        Guid? modeId = null,
        string? ipSlug = null,
        string? modeCode = null,
        int limit = 50,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Safe limit protection: between 1 and 100 rows, skip >= 0
            var safeLimit = Math.Clamp(limit, 1, 100);
            var safeSkip = Math.Max(0, skip);
            var cs = config.GetRequiredPostgresConnectionString();
            await using var connection = new NpgsqlConnection(cs);

            var parameters = new
            {
                IpId = ipId,
                ModeId = modeId,
                IpSlug = ipSlug,
                ModeCode = modeCode,
                Skip = safeSkip,
                Limit = safeLimit
            };

            var command = new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<LeaderboardRowDto>(command);

            return results.ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute GetLeaderboardAsync query with window functions");
            throw;
        }
    }
}
