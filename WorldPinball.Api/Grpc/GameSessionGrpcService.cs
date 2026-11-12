using WorldPinball.Protos.V1.GameSessions;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer.Commands;
using Grpc.Core;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Dtos;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Queries;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer.Commands;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.RecordBallScore;
using WorldPinball.Application.Features.GameSessions.Public.RecordBallScore.Commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Interfaces.Services;
using AddPlayerRequest = WorldPinball.Protos.V1.GameSessions.AddPlayerRequest;
using AddPlayerResponse = WorldPinball.Protos.V1.GameSessions.AddPlayerResponse;
using KickPlayerResponse = WorldPinball.Protos.V1.GameSessions.KickPlayerResponse;
using LaunchSessionResponse = WorldPinball.Protos.V1.GameSessions.LaunchSessionResponse;

namespace WorldPinball.Api.Grpc;

public sealed class GameSessionGrpcService(
    IAddPlayerHandler addPlayerHandler,
    IKickPlayerHandler kickPlayerHandler,
    IGetLobbyHandler getLobbyHandler,
    IRecordBallScoreHandler recordBallScoreHandler,
    ILaunchSessionHandler launchSessionHandler,
    ILobbyNotificationService lobbyNotifier,
    ILogger<GameSessionGrpcService> logger,
    ILiveTelemetryService liveTelemetryService,
    IGameSessionReadRepository gameSessionReadRepository
) : GameSessionService.GameSessionServiceBase
{
    public override async Task<AddPlayerResponse> AddPlayer(
        AddPlayerRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid session ID format"));

        Guid? userId = request.HasUserId && Guid.TryParse(request.UserId, out var parsed) ? parsed : null;
        string? initials = request.HasGuestInitials ? request.GuestInitials : null;

        var command = new AddPlayerCommand(sessionId, request.SlotNumber, userId, initials);
        var result = await addPlayerHandler.HandleAsync(command, context.CancellationToken);

        if (result is AddPlayerResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        logger.LogInformation("Added player to slot {Slot} for session {SessionId}.", command.SlotNumber,
            command.SessionId);

        return result switch
        {
            AddPlayerResult.Success s =>
                new AddPlayerResponse
                {
                    Success = true, SlotNumber =
                        s.SlotNumber,
                    Message = "Player added."
                },

            AddPlayerResult.SessionNotFound =>
                throw new RpcException(new Status(StatusCode.NotFound,
                    "Session not found.")),

            AddPlayerResult.SessionNotJoinable =>
                throw new RpcException(
                    new Status(StatusCode.FailedPrecondition,
                        "Session is not joinable.")),

            AddPlayerResult.SlotAlreadyOccupied =>
                throw new RpcException(new Status(StatusCode.AlreadyExists,
                    "Slot is already occupied.")),

            AddPlayerResult.InvalidSlot =>
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "Slot number must be between 1 and 4.")),

            AddPlayerResult.RegisteredUserRequired =>
                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    "Registered user required.")),

            AddPlayerResult.UserAlreadyRegistered =>
                throw new RpcException(new Status(StatusCode.AlreadyExists,
                    "User already registered.")),

            _ => throw new RpcException(new Status(StatusCode.Internal,
                "Unexpected error."))
        };
    }


    public override async Task<KickPlayerResponse> KickPlayer(KickPlayerRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Invalid session ID format"));

        var command = new KickPlayerCommand(
            sessionId,
            request.SlotNumber,
            request.HasReason ? request.Reason : null);
        var result = await kickPlayerHandler.HandleAsync(command, context.CancellationToken);

        if (result is KickPlayerResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            KickPlayerResult.Success s =>
                new KickPlayerResponse
                {
                    Success = true, SlotNumber =
                        s.SlotNumber,
                    Message =
                        "Player kicked."
                },

            KickPlayerResult.SessionNotFound =>
                throw new RpcException(new Status(StatusCode.NotFound, "Session not found.")),

            KickPlayerResult.CannotRemovePlayerActiveSession =>
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    "Cannot remove player once session has started or ended.")),

            KickPlayerResult.PlayerNotFound =>
                throw new RpcException(new Status(StatusCode.NotFound,
                    "Player not found in the specified slot.")),

            KickPlayerResult.InvalidSlot =>
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "Slot number must be between 1 and 4.")),

            //Default
            _ => throw new RpcException(new Status(StatusCode.Internal, "Unexpected error."))
        };
    }

    public override async Task<LaunchSessionResponse> LaunchSession(LaunchSessionRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Invalid session ID format"));

        var command = new LaunchSessionCommand(sessionId);
        var result = await launchSessionHandler.HandleAsync(command, context.CancellationToken);

        if (result is LaunchSessionResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            LaunchSessionResult.Success s =>
                new LaunchSessionResponse
                    { Success = true, SessionId = s.SessionId.ToString(), Message = "Session launched." },

            LaunchSessionResult.SessionNotFound =>
                throw new RpcException(new Status(StatusCode.NotFound, "Session not found.")),

            LaunchSessionResult.CannotStartSession e =>
                throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)),

            _ => throw new RpcException(new Status(StatusCode.Internal, "Unexpected error."))
        };
    }

    public override async Task<SessionLobbyResponse> GetLobby(GetLobbyRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Invalid session ID format"));

        var query = new GetLobbyQuery(sessionId);
        var result = await getLobbyHandler.HandleAsync(query, context.CancellationToken);

        return result switch
        {
            GetLobbyResult.Success s => MapToLobbyResponse(s.Lobby),

            GetLobbyResult.SessionNotFound =>
                throw new RpcException(new Status(StatusCode.NotFound, "Session not found.")),

            _ => throw new RpcException(new Status(StatusCode.Internal, "Unexpected error."))
        };
    }


    //Mapping
    private static SessionLobbyResponse MapToLobbyResponse(SessionLobbyDto lobby)
    {
        var response = new SessionLobbyResponse
        {
            SessionId = lobby.SessionId.ToString(),
            SessionState = lobby.SessionState
        };

        foreach (var slot in lobby.Slots)
        {
            var slotInfo = new PlayerSlotInfo
            {
                SlotNumber = slot.SlotNumber,
                IsOccupied = slot.IsOccupied,
                Status = (SlotStatus)(int)slot.Status
            };

            if (slot.UserId.HasValue)
                slotInfo.UserId = slot.UserId.Value.ToString();

            if (!string.IsNullOrWhiteSpace(slot.GuestInitials))
                slotInfo.GuestInitials = slot.GuestInitials;

            response.Slots.Add(slotInfo);
        }

        return response;
    }


    public override async Task WatchLobby(
        WatchLobbyRequest request,
        IServerStreamWriter<SessionLobbyResponse> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Invalid session ID format"));

        // send initial snapshot
        var initialResult = await getLobbyHandler.HandleAsync(new GetLobbyQuery(sessionId), context.CancellationToken);
        if (initialResult is not GetLobbyResult.Success initialSuccess)
            throw new RpcException(new Status(StatusCode.NotFound, "Session not found."));

        await responseStream.WriteAsync(MapToLobbyResponse(initialSuccess.Lobby));


        if (IsTerminalState(initialSuccess.Lobby.SessionState)) return;

        await foreach (var _ in lobbyNotifier.SubscribeAsync(sessionId, context.CancellationToken))
        {
            var result = await getLobbyHandler.HandleAsync(new GetLobbyQuery(sessionId), context.CancellationToken);
            
            if (result is not GetLobbyResult.Success success) continue;
            await responseStream.WriteAsync(MapToLobbyResponse(success.Lobby));
            if (IsTerminalState(success.Lobby.SessionState)) break;
        }
    }


    public override async Task<RecordBallScoreResponse> RecordBallScore(RecordBallScoreRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId) ||
            !Guid.TryParse(request.DeviceId, out var deviceId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Invalid SessionId or DeviceId format."));
        var command = new RecordBallScoreCommand(sessionId, deviceId,
            request.SlotNumber, request.BallNumber, request.Score);

        var result = await recordBallScoreHandler.HandleAsync(command, context.CancellationToken);

        if (result is RecordBallScoreResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            RecordBallScoreResult.Success s =>
                new RecordBallScoreResponse
                {
                    Success = true,
                    SessionId = s.SessionId.ToString(),
                    SlotNumber = s.SlotNumber,
                    BallNumber = s.BallNumber,
                    Score = s.Score,
                    Message = "Ball score recorded."
                },

            RecordBallScoreResult.SessionNotFound => throw new RpcException(
                new Status(StatusCode.NotFound, "Session not found.")),

            RecordBallScoreResult.DeviceMisMatch => throw new RpcException(
                new Status(StatusCode.PermissionDenied, "Device mismatch.")),

            RecordBallScoreResult.CannotRecordBallScore c => throw
                new RpcException(new Status(StatusCode.FailedPrecondition, c.Reason)),

            _ => throw new RpcException(new Status(
                StatusCode.Internal, "Unexpected error."))
        };
    }


    public override async Task<StreamingSummaryResponse> StreamLiveTelemetry(
        IAsyncStreamReader<ScoreDeltaRequest> requestStream,
        ServerCallContext context)
    {
        long totalScore = 0;
        int eventsProcessed = 0;
        Guid sessionId = Guid.Empty;
        int slotNumber = 0;

        // Read incoming deltas continuously from the HTTP/2 stream
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var delta = requestStream.Current;
            if (!Guid.TryParse(delta.SessionId, out sessionId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid SessionId format."));

            slotNumber = delta.SlotNumber;
            totalScore = await liveTelemetryService.IngestDeltaAsync(sessionId, delta.SlotNumber, delta.PointsDelta,
                delta.TriggerEvent, context.CancellationToken);
            eventsProcessed++;
        }

        return new StreamingSummaryResponse
        {
            Success = true,
            SessionId = sessionId.ToString(),
            SlotNumber = slotNumber,
            TotalStreamedScore = totalScore,
            TotalEventsProcessed = eventsProcessed,
            Message = "Live telemetry stream completed successfully."
        };
    }


    public override async Task WatchAllLobbies(
        WatchAllLobbiesRequest request,
        IServerStreamWriter<SessionLobbyResponse> responseStream,
        ServerCallContext context)
    {
        // subscription
        var stream = lobbyNotifier.SubscribeAllAsync(
            context.CancellationToken);
        await using var enumerator = stream
            .GetAsyncEnumerator(
            context.CancellationToken);

        // local memory of stream to dedup same sending
        var lastFingerprints = new Dictionary<string, string>();
        
        // Initial Snapshot
        var activeSessionIds = await gameSessionReadRepository
            .GetActiveSessionIdsAsync(context.CancellationToken);

        foreach (var sessionId in activeSessionIds)
        {
            var initialResult =
                await getLobbyHandler.HandleAsync(new GetLobbyQuery(sessionId),
                    context.CancellationToken);
            
            if (initialResult is GetLobbyResult.Success success)
            {
                await SendIfNotDuplicateAsync(success.Lobby, lastFingerprints, responseStream);
            }
        }

        //  Real-time stream: broadcast every update emitted by any pinball machine

        while (await enumerator.MoveNextAsync())
        {
            var sessionId = enumerator.Current;
            var result = await getLobbyHandler.HandleAsync(
                new GetLobbyQuery(sessionId),
                context.CancellationToken);
            if (result is GetLobbyResult.Success success)
            {
                await SendIfNotDuplicateAsync(success.Lobby, lastFingerprints, responseStream);
            }

        }

    }

    private static async Task SendIfNotDuplicateAsync(
        SessionLobbyDto lobby,
        Dictionary<string, string> lastFingerprints,
        IServerStreamWriter<SessionLobbyResponse> responseStream)
    {
        var response = MapToLobbyResponse(lobby);
        var fingerprint = $"{response.SessionState}_{string.Join(",", response.Slots.Select(s => $"{s.SlotNumber}:{s.Status}:{s.UserId}"))}";
        if (!lastFingerprints.TryGetValue(response.SessionId, out var previous) || previous != fingerprint)
        {
            lastFingerprints[response.SessionId] = fingerprint;
            await responseStream.WriteAsync(response);
        }
    }

    private static bool IsTerminalState(string state) =>
        state is "Ended" or "Closed" or "Cancelled" or "Abandoned" or "Aborted";
    
}