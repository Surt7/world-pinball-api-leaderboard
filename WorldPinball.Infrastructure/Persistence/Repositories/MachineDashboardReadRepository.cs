using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WorldPinball.Application.Features.Leaderboard.Admin.Dashboard.Dtos;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Infrastructure.Persistence.Repositories;

public sealed class MachineDashboardReadRepository(IConfiguration config) : IMachineDashboardReadRepository
{
    private const string Sql = """
        WITH session_stats AS (
            SELECT 
                COUNT(*) AS total_games,
                MAX(started_at_utc) AS last_played_at
            FROM public.game_sessions
            WHERE machine_id = @MachineId
        ),
        score_stats AS (
            SELECT 
                MAX(score) AS high_score,
                AVG(score) AS average_score,
                COUNT(DISTINCT user_id) AS total_unique_players
            FROM public.score_entries
            WHERE machine_id = @MachineId
        )
        SELECT
            m.machine_id AS MachineId,
            m.world_pinball_serial_number AS SerialNumber,
            COALESCE(ss.total_games, 0)::int AS TotalGamesPlayed,
            COALESCE(sc.high_score, 0)::bigint AS HighScore,
            COALESCE(sc.average_score, 0)::bigint AS AverageScore,
            COALESCE(sc.total_unique_players, 0)::int AS TotalUniquePlayers,
            ss.last_played_at AS LastPlayedAtUtc
        FROM public.machines m
        LEFT JOIN session_stats ss ON true
        LEFT JOIN score_stats sc ON true
        WHERE m.machine_id = @MachineId;
        """;

    public async Task<MachineDashboardDto?> GetMachineStatsAsync(Guid machineId, CancellationToken ct = default)
    {
        var cs = config.GetRequiredPostgresConnectionString();
        await using var connection = new NpgsqlConnection(cs);

        var command = new CommandDefinition(Sql, new { MachineId = machineId }, cancellationToken: ct);
        return await connection.QuerySingleOrDefaultAsync<MachineDashboardDto>(command);
    }
}