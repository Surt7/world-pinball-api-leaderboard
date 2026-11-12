using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using WorldPinball.Bff.Hubs;
using WorldPinball.Bff.Logging;
using WorldPinball.Protos.V1.GameSessions;

namespace WorldPinball.Bff.Services;

public sealed class LobbyRelayService(
    GameSessionService.GameSessionServiceClient grpcClient,
    IHubContext<LobbyHub> hubContext,
    ILogger<LobbyRelayService> logger
    ) : ILobbyRelayService
{
    private readonly ConcurrentDictionary<string, SessionLobbyResponse> _cachedLobbies = new();
    private readonly ConcurrentDictionary<Guid, byte> _activeSessions = new();
    private int _isWatchingAll;
    
    public IReadOnlyCollection<SessionLobbyResponse> GetCurrentLobbies() =>
        _cachedLobbies.Values.ToList();
    
    public async Task EnsureWatchingLobbyAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // Atomic check-and-insert: if already being watched by another client,
        // skip starting a new stream.
        if (!_activeSessions.TryAdd(sessionId, 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var request = new WatchLobbyRequest { SessionId = sessionId.ToString() };
                using var call = grpcClient.WatchLobby(request, cancellationToken: cancellationToken);

                while (await call.ResponseStream.MoveNext(cancellationToken))
                {
                    var lobby = call.ResponseStream.Current;
                    _cachedLobbies[lobby.SessionId] = lobby;

                    await hubContext.Clients.Group(sessionId.ToString())
                        .SendAsync("LobbyStateUpdated", lobby, cancellationToken);
                    
                    await hubContext.Clients.Group("AdminSupervision")
                        .SendAsync("LobbyStateUpdated", lobby, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    BffLogEvents.LobbyRelayStreamError,
                    ex,
                    "Error while watching lobby stream for session {SessionId}",
                    sessionId);
            }
            finally
            {
                _activeSessions.TryRemove(sessionId, out _); 
            }
        }, cancellationToken);

        await Task.CompletedTask;
    }

    public async Task EnsureWatchingAllLobbiesAsync(CancellationToken cancellationToken)
    {
        // Atomic check: ensure only one background stream runs for the entire server
        if (Interlocked.CompareExchange(ref _isWatchingAll, 1, 0) != 0)
            return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var request = new WatchAllLobbiesRequest();
                using var call = grpcClient.WatchAllLobbies(request, cancellationToken: cancellationToken);

                while (await call.ResponseStream.MoveNext(cancellationToken))
                {
                    var lobby = call.ResponseStream.Current;
                    _cachedLobbies[lobby.SessionId] = lobby;

                    await hubContext.Clients.Group("AdminSupervision")
                        .SendAsync("LobbyStateUpdated", lobby, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    BffLogEvents.LobbyRelayAllStreamError,
                    ex,
                    "Error while watching all lobbies stream");
            }
            finally
            {
                Interlocked.Exchange(ref _isWatchingAll, 0);
            }
        }, cancellationToken);

        await Task.CompletedTask;
    }
}
