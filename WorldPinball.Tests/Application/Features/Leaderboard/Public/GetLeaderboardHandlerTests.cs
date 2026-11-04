using FluentAssertions;
using NSubstitute;
using WorldPinball.Application.Features.Leaderboard.Public;
using WorldPinball.Application.Features.Leaderboard.Public.Dtos;
using WorldPinball.Application.Features.Leaderboard.Public.Queries;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Interfaces.Services;

namespace WorldPinball.Tests.Application.Features.Leaderboard.Public;

public class GetLeaderboardHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenCacheHasScores_ShouldReturnFromCacheWithoutQueryingDb()
    {
        
        // Arrange
        var repository = Substitute.For<ILeaderboardReadRepository>();
        var realtimeService = Substitute.For<IRealtimeLeaderboardService>();
        
        var ipId = Guid.NewGuid();
        var modeId = Guid.NewGuid();

        var query = new GetLeaderboardQuery(IpId: ipId, ModeId: modeId, Limit: 10);
        
        var cachedRows = new List<LeaderboardRowDto>
        {
            new(1, Guid.NewGuid(), "TOM", "Thomas", 150_000_000, null, 0, DateTime.UtcNow, "Star Wars", "Classic"),
            new(2, Guid.NewGuid(), "BOB", "Bobby", 90_000_000, 60_000_000, 60_000_000, DateTime.UtcNow, "Star Wars", "Classic")
        };
        realtimeService.GetCachedLeaderboardAsync(
                ipId, modeId, 10, Arg.Any<CancellationToken>())
            .Returns(cachedRows);
        
        //Act
        var sut = new GetLeaderboardHandler(repository, realtimeService);
        
        var result = await sut.HandleAsync(query);
        result.Should().BeOfType<GetLeaderboardResult.Success>()
            .Which.Rows.Should().HaveCount(2);
  
        await repository.DidNotReceive().GetLeaderboardAsync(
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        );
    }





    [Fact]
    public async Task HandleAsync_WhenCacheMiss_ShouldQueryDatabaseAndWarmupCache()
    {
        var repository = Substitute.For<ILeaderboardReadRepository>();
        var realtimeService = Substitute.For<IRealtimeLeaderboardService>();
    
        var ipId = Guid.NewGuid();
        var modeId = Guid.NewGuid();
        var query = new GetLeaderboardQuery(IpId: ipId, ModeId: modeId, Limit: 10);
        
        
        realtimeService.GetCachedLeaderboardAsync(
                ipId, modeId, 10, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LeaderboardRowDto>?)null);
        
        
        var dbRows = new List<LeaderboardRowDto>
        {
            new(1, Guid.NewGuid(), "TOM", "Thomas", 150_000_000, null, 0, DateTime.UtcNow, "Star Wars", "Classic")
        };
        repository.GetLeaderboardAsync(
                ipId, modeId, null, null, 10, 0, Arg.Any<CancellationToken>())
            .Returns(dbRows);
        
        var sut = new GetLeaderboardHandler(repository, realtimeService);
        var result = await sut.HandleAsync(query);
            
            
        result.Should().BeOfType<GetLeaderboardResult.Success>()
            .Which.Rows.Should().BeEquivalentTo(dbRows);
        
        await realtimeService.Received(1).SetCachedLeaderboardAsync(
            ipId, modeId, 10, dbRows,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>());
    }
    
    
}