namespace WorldPinball.Application.Features.Leaderboard.Public.Dtos;

public sealed record LeaderboardRowDto(
    int Rank,
    Guid ScoreEntryId,
    string PlayerInitials,
    string PlayerName,
    long Score,
    long? PointsBehindPrevious,
    long PointsBehindLeader,
    DateTime AchievedAtUtc,
    string IpName,
    string ModeName
);