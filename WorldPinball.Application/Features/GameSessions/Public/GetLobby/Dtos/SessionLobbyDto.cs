namespace WorldPinball.Application.Features.GameSessions.Public.GetLobby.Dtos;

public sealed record LobbySlotDto(
    int SlotNumber,
    bool IsOccupied,
    GameSlotStatus Status,
    Guid? UserId = null,
    string? GuestInitials = null
);

public sealed record SessionLobbyDto(
    Guid SessionId,
    string SessionState,
    IReadOnlyList<LobbySlotDto> Slots
);