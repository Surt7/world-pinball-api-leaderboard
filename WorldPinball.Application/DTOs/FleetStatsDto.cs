namespace WorldPinball.Application.DTOs;

public record FleetStatsDto(
    int Total,
    int Online,
    int Offline,
    int Maintenance,
    int Pending
);
