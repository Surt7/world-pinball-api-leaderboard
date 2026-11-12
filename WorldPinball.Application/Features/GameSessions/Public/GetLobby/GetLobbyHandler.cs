using Microsoft.Extensions.Logging;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Dtos;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Queries;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Application.Features.GameSessions.Public.GetLobby;

public sealed class GetLobbyHandler(
    IGameSessionReadRepository readRepository,
    ILogger<GetLobbyHandler> logger
) : IGetLobbyHandler
{
    private static readonly EventId SessionNotFound = new EventId(2700, nameof(SessionNotFound));

    public async Task<GetLobbyResult> HandleAsync(GetLobbyQuery query, CancellationToken cancellationToken = default)
    {
        var session = await readRepository.GetSessionForLobbyAsync(
            query.SessionId,
            cancellationToken);


        if (session is null)
        {
            logger.LogWarning(
                SessionNotFound,
                "Game session {SessionId} could not be found.",
                query.SessionId);
            return new GetLobbyResult.SessionNotFound(query.SessionId);
        }


        var slots = session.Slots.Select(slot => new LobbySlotDto(
            SlotNumber: slot.SlotNumber,
            IsOccupied: slot.Status == GameSlotStatus.Occupied,
            Status: slot.Status,
            UserId: slot.UserId,
            GuestInitials: slot.GuestInitials
        )).ToList();

        var lobbyDto = new SessionLobbyDto(
            session.SessionId,
            session.State.ToString(),
            slots);
        return new GetLobbyResult.Success(lobbyDto);
    }
}