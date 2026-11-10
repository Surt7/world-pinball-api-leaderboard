using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldPinball.Api.Contracts.Sessions;
using WorldPinball.Api.Extensions;
using WorldPinball.Api.Filters.Security;
using WorldPinball.Application.Features.GameSessions.Public.AbortSession;
using WorldPinball.Application.Features.GameSessions.Public.AbortSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer.Commands;
using WorldPinball.Application.Features.GameSessions.Public.CreateGameSession;
using WorldPinball.Application.Features.GameSessions.Public.CreateGameSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Dtos;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Queries;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer.Commands;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.SubmitScore;
using WorldPinball.Application.Features.GameSessions.Public.SubmitScore.Commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.Cryptography;
using WorldPinball.Application.Interfaces.Services;
using WorldPinball.Infrastructure.Security.Authorization;

namespace WorldPinball.Api.Controllers.Public;

[ApiController]
[Route("api/v1/sessions")]
public class SessionsController(
    ICreateGameSessionHandler createGameSessionHandler,
    ISubmitScoreHandler submitScoreHandler,
    IAbortSessionHandler abortSessionHandler,
    IGetLobbyHandler getLobbyHandler,
    IAddPlayerHandler addPlayerHandler,
    IKickPlayerHandler kickPlayerHandler,
    ILaunchSessionHandler launchSessionHandler,
    ILobbyNotificationService lobbyNotifier,
    IGameSessionWriteRepository sessionRepository,
    IAuthorizationService authorization
) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

  
    
    [HttpPost("create")]
    [RequireMachineSignature] 
    [EndpointSummary("Create a game session")]
    [EndpointDescription("POST to create new game session")]
    [ProducesResponseType(typeof(CreateGameSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateGameSessionResponse>> CreateGameSession(
        [FromBody] CreateGameSessionRequest request,
        CancellationToken ct)
    {

        var deviceId = HttpContext.GetAuthenticatedDeviceId();

        
        var command = new CreateGameSessionCommand(
            deviceId,
            request.ModeCode,
            request.ClientVersion
        );

        var sessionResult = await createGameSessionHandler.HandleAsync(command, ct);

        if (sessionResult is CreateGameSessionResult.Success successSession)
        {
            lobbyNotifier.NotifyLobbyUpdated(successSession.SessionId);
        }

        return sessionResult switch
        {
            CreateGameSessionResult.Success s => Ok(new CreateGameSessionResponse(
                s.SessionId,
                s.CreatedAtUtc.ToUnixTimeSeconds()
            )),
            CreateGameSessionResult.ModeNotFound m => NotFound(new
            {
                message = $"Mode '{m.ModeCode}' not found or inactive."
            }),
            CreateGameSessionResult.DeviceNotFound d => NotFound(new
            {
                message = $"Device '{d.DeviceId}' not found."
            }),
            CreateGameSessionResult.DeviceNotActive d => BadRequest(new
            {
                message = $"Device '{d.DeviceId}' is not active."
            }),
            CreateGameSessionResult.MachineNotAttached d => BadRequest(new
            {
                message = $"Device '{d.DeviceId}' is not attached to any machine."
            }),
            _ => throw new InvalidOperationException($"Unsupported session result: {sessionResult.GetType().Name}")
        };
    }

    [HttpGet("{sessionId:guid}/lobby")]
    [EndpointSummary("Get lobby status")]
    [EndpointDescription("GET current lobby slots and player information")]
    [ProducesResponseType(typeof(SessionLobbyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionLobbyDto>> GetLobby(
        [FromRoute] Guid sessionId,
        CancellationToken ct)
    {
        var result = await getLobbyHandler.HandleAsync(new GetLobbyQuery(sessionId), ct);
        return result switch
        {
            GetLobbyResult.Success s => Ok(s.Lobby),
            GetLobbyResult.SessionNotFound s => NotFound(new { message = $"Session '{s.SessionId}' not found." }),
            _ => throw new InvalidOperationException($"Unsupported get lobby result: {result.GetType().Name}")
        };
    }

    [HttpPost("{sessionId:guid}/players")]
    [EndpointSummary("Add player to session")]
    [EndpointDescription("POST to assign a player (user or guest) to a lobby slot")]
    [ProducesResponseType(typeof(AddPlayerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AddPlayerResponse>> AddPlayer(
        [FromRoute] Guid sessionId,
        [FromBody] AddPlayerRequest request,
        CancellationToken ct)
    {

        var command = new AddPlayerCommand(
            sessionId,
            request.SlotNumber,
            request.UserId,
            request.GuestInitials,
            request.RequiresRegisteredUser);

        var result = await addPlayerHandler.HandleAsync(command, ct);
        if (result is AddPlayerResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            AddPlayerResult.Success s => Ok(new AddPlayerResponse(s.SessionId, s.SlotNumber)),
            AddPlayerResult.SessionNotFound s => NotFound(new { message = $"Session '{s.SessionId}' not found." }),
            AddPlayerResult.SessionNotJoinable s => Conflict(new { message = $"Session '{s.SessionId}' is not joinable." }),
            AddPlayerResult.InvalidSlot s => BadRequest(new { message = $"Slot '{s.SlotNumber}' is invalid." }),
            AddPlayerResult.SlotAlreadyOccupied s => Conflict(new { message = $"Slot '{s.SlotNumber}' is already occupied." }),
            AddPlayerResult.RegisteredUserRequired => BadRequest(new { message = "Registered user is required." }),
            AddPlayerResult.UserAlreadyRegistered u => Conflict(new { message = $"User '{u.UserId}' is already in session." }),
            _ => throw new InvalidOperationException($"Unsupported add player result: {result.GetType().Name}")
        };
    }

    [HttpDelete("{sessionId:guid}/players/{slotNumber:int}")]
    [Authorize]
    [EndpointSummary("Kick player from session")]
    [EndpointDescription("DELETE a player from a lobby slot")]
    [ProducesResponseType(typeof(KickPlayerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<KickPlayerResponse>> KickPlayer(
        [FromRoute] Guid sessionId,
        [FromRoute] int slotNumber,
        CancellationToken ct)
    {
        // Check if player have requirement
        var session = await sessionRepository.GetSessionWithMachineAsync(sessionId, ct);
        if (session is null) return NotFound(new { message = $"Session '{sessionId}' not found." });
        
        var auth = await authorization.AuthorizeAsync(User, session, new GameSessionParticipantRequirement());
        if (!auth.Succeeded) return Forbid();
        
        
        
        var command = new KickPlayerCommand(sessionId, slotNumber);
        var result = await kickPlayerHandler.HandleAsync(command, ct);
        if (result is KickPlayerResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            KickPlayerResult.Success s => Ok(new KickPlayerResponse(s.SessionId, s.SlotNumber)),
            KickPlayerResult.SessionNotFound s => NotFound(new { message = $"Session '{s.SessionId}' not found." }),
            KickPlayerResult.PlayerNotFound s => NotFound(new { message = $"Player not found on slot '{s.SlotNumber}'." }),
            KickPlayerResult.InvalidSlot s => BadRequest(new { message = $"Slot '{s.SlotNumber}' is invalid." }),
            KickPlayerResult.CannotRemovePlayerActiveSession s => Conflict(new { message = $"Cannot kick player in active session '{s.SessionId}'." }),
            _ => throw new InvalidOperationException($"Unsupported kick player result: {result.GetType().Name}")
        };
    }
    
    
    [HttpPost("{sessionId:guid}/launch")]
    [Authorize]
    [EndpointSummary("Launch game session")]
    [EndpointDescription("POST to launch/start an existing lobby session")]
    [ProducesResponseType(typeof(LaunchSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LaunchSessionResponse>> LaunchSession(
        [FromRoute] Guid sessionId,
        CancellationToken ct)
    {
        
        // Check if got the right with requirement
        var session = await sessionRepository.GetSessionWithMachineAsync(sessionId, ct);
        if (session is null) return NotFound(new { message = $"Session '{sessionId}' not found." });
        var auth = await authorization.AuthorizeAsync(User, session, new GameSessionParticipantRequirement());
        if (!auth.Succeeded) return Forbid();
        
        
        var command = new LaunchSessionCommand(sessionId);
        var result = await launchSessionHandler.HandleAsync(command, ct);
        if (result is LaunchSessionResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            LaunchSessionResult.Success s => Ok(new LaunchSessionResponse(s.SessionId)),
            LaunchSessionResult.SessionNotFound s => NotFound(new { message = $"Session '{s.SessionId}' not found." }),
            LaunchSessionResult.CannotStartSession s => BadRequest(new { message = s.Message }),
            _ => throw new InvalidOperationException($"Unsupported launch session result: {result.GetType().Name}")
        };
    }

    [HttpPost("{sessionId:guid}/submit-score")]
    [Authorize(Policy = "ActiveMachineOnly")]
    [EndpointSummary("Submit Score Request")]
    [EndpointDescription("POST to create new score from a game session that just ended")]
    [ProducesResponseType(typeof(SubmitScoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubmitScoreResponse>> SubmitScore(
        [FromRoute] Guid sessionId,
        [FromBody] SubmitScoreRequest request,
        CancellationToken ct)
    {

        var command = new SubmitScoreCommand(
            sessionId,
            request.DeviceId,
            request.Scores.Select(s => new SlotScoreDto(s.SlotNumber, s.Score)).ToList());

        var scoreResult = await submitScoreHandler.HandleAsync(command, ct);
        if (scoreResult is SubmitScoreResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return scoreResult switch
        {
            SubmitScoreResult.Success s => Ok(new SubmitScoreResponse(s.ScoreEntryIds)),
            SubmitScoreResult.SessionNotFound s => NotFound(new
            {
                message = $"Session '{s.GameSessionId}' not found."
            }),
            SubmitScoreResult.SessionAlreadyEnded s => Conflict(new
            {
                message = $"Session '{s.GameSessionId}' already ended."
            }),
            SubmitScoreResult.SessionNotStarted s => Conflict(new
            {
                message = $"Session '{s.GameSessionId}' not started."
            }),
            SubmitScoreResult.DeviceMisMatch s => Conflict(new
            {
                message = $"Device '{s.ActualDeviceId}' mismatch (expected '{s.ExpectedDeviceId}')."
            }),
            SubmitScoreResult.SlotNotFound s => Conflict(new
            {
                message = $"Slot number: '{s.SlotNumber}' not found."
            }),
            SubmitScoreResult.EmptyScores => BadRequest(new
            {
                message = "Scores list cannot be empty."
            }),
            _ => throw new InvalidOperationException($"Unsupported session result: {scoreResult.GetType().Name}")
        };
    }

    [HttpPost("{sessionId:guid}/abort")]
    [Authorize]
    [EndpointSummary("Abort game session")]
    [EndpointDescription("POST to abort an active game session")]
    [ProducesResponseType(typeof(AbortSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AbortSessionResponse>> AbortSession(
        [FromRoute] Guid sessionId,
        CancellationToken ct)
    {
        var session = await sessionRepository.GetSessionWithMachineAsync(sessionId, ct);
        if (session is null)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }
        
        var auth = await authorization.AuthorizeAsync(
            User, session, new GameSessionParticipantRequirement());
        if (!auth.Succeeded)
        {
            return Forbid();
        }
        
        var command = new AbortSessionCommand(sessionId);
        var result = await abortSessionHandler.HandleAsync(command, ct);
        if (result is AbortSessionResult.Success)
        {
            lobbyNotifier.NotifyLobbyUpdated(sessionId);
        }

        return result switch
        {
            AbortSessionResult.Success s => Ok(new AbortSessionResponse(s.SessionId)),
            AbortSessionResult.SessionNotFound s => NotFound(new { message = $"Session '{s.SessionId}' not found." }),
            AbortSessionResult.CannotAbortSession s => BadRequest(new { message = s.Reason }),
            _ => throw new InvalidOperationException($"Unsupported abort session result: {result.GetType().Name}")
        };
    }


}