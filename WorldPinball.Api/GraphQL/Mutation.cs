using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer.Commands;
using WorldPinball.Application.Interfaces.Services;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;
using WorldPinball.Infrastructure.Persistence;

namespace WorldPinball.Api.GraphQL;

public record ClaimMachinePayload(bool Success, string Message, Machine? Machine);

public record JoinGameSessionPayload(
    bool Success, 
    string Message, 
    int? SlotNumber, 
    string? MachineName
);

public class Mutation
{
    public async Task<ClaimMachinePayload> ClaimMachineAsync(
        string code,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        // 1. Resolve authenticated user's ID
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
               ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
        {
            return new ClaimMachinePayload(false, "Utilisateur non authentifié.", null);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return new ClaimMachinePayload(false, "Veuillez fournir un code d'activation.", null);
        }

        var trimmedCode = code.Trim();

        // 2. Normalize code to find device
        // Supported patterns:
        // - "WP-00004" -> matches "DEVICE-00004"
        // - "WP-4"     -> matches "DEVICE-00004"
        // - "DEVICE-00004" -> matches "DEVICE-00004"
        // - "00004" / "4" -> matches "DEVICE-00004"
        string? targetSerial = null;
        if (trimmedCode.StartsWith("WP-", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = trimmedCode.Substring(3).Trim();
            if (int.TryParse(numStr, out var num))
            {
                targetSerial = $"DEVICE-{num:00000}";
            }
        }
        else if (trimmedCode.StartsWith("DEVICE-", StringComparison.OrdinalIgnoreCase))
        {
            targetSerial = trimmedCode.ToUpperInvariant();
        }
        else if (int.TryParse(trimmedCode, out var num))
        {
            targetSerial = $"DEVICE-{num:00000}";
        }

        // 3. Find device in database
        var device = await db.Devices
            .Include(d => d.MachineDevices)
            .ThenInclude(md => md.Machine)
            .FirstOrDefaultAsync(d => 
                d.SerialPhysical == trimmedCode || 
                (targetSerial != null && d.SerialPhysical == targetSerial));

        if (device == null)
        {
            return new ClaimMachinePayload(false, "Aucune machine trouvée pour ce code d'activation.", null);
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return new ClaimMachinePayload(false, "Ce flipper est désactivé ou révoqué.", null);
        }

        // 4. Check ownership
        if (device.OwnerUserId.HasValue)
        {
            if (device.OwnerUserId.Value == userId)
            {
                var currentMachine = device.MachineDevices
                    .FirstOrDefault(md => md.IsActive)?.Machine;
                return new ClaimMachinePayload(false, "Vous possédez déjà ce flipper.", currentMachine);
            }

            return new ClaimMachinePayload(false, "Cette machine est déjà associée à un autre propriétaire.", null);
        }

        // 5. Assign owner
        device.AssignOwner(userId);
        await db.SaveChangesAsync();

        var activeMachine = device.MachineDevices
            .FirstOrDefault(md => md.IsActive)?.Machine;

        var machineName = activeMachine?.DisplayName ?? device.SerialPhysical;

        return new ClaimMachinePayload(
            true, 
            $"Le flipper \"{machineName}\" a été relié avec succès à votre compte !", 
            activeMachine);
    }


    // ==========================================
    // 2. New mutation: JoinGameSession
    // ==========================================
    // Join an active game session from pinball cabinet or session code
    public async Task<JoinGameSessionPayload> JoinGameSessionAsync(
        string code,
        [Service] AppDbContext db,
        [Service] IAddPlayerHandler addPlayerHandler,
        [Service] ILobbyNotificationService lobbyNotifier,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        
        // 1. Resolve authenticated user ID from JWT token claims
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
                  ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
        {
            return new JoinGameSessionPayload(false, "Authentication required to join a game.", null, null);
        }
        
        // 2. Validate input code
        if (string.IsNullOrWhiteSpace(code))
        {
            return new JoinGameSessionPayload(false, "Session or machine code cannot be empty.", null, null);
        }
        
        var trimmedCode = code.Trim();
        // 3. Prepare target device serial if user entered a pinball code (e.g., "WP-00008", "WP-8", or "8")
        string? targetSerial = null;
        
        if (trimmedCode.StartsWith("WP-", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = trimmedCode.Substring(3).Trim();
            if (int.TryParse(numStr, out var num))
            {
                targetSerial = $"DEVICE-{num:00000}";
            }
        }
        else if (int.TryParse(trimmedCode, out var num))
        {
            targetSerial = $"DEVICE-{num:00000}";
        }
        else
        {
            targetSerial = trimmedCode.ToUpperInvariant();
        }

        GameSession? session = null;
        
        // Path A: User scanned a direct session GUID (from QR Code)
        if (Guid.TryParse(trimmedCode, out var directSessionId))
        {
            session = await db.GameSessions
                .Include(s => s.Machine)
                .Include(s => s.Players)
                .FirstOrDefaultAsync(s => s.SessionId == directSessionId);
        }
        
        // Path B: User entered machine code (e.g., "WP-00008") -> find active session on this cabinet
        if (session == null)
        {
            session = await db.GameSessions
                .Include(s => s.Machine)
                .Include(s => s.Players)
                .Where(s => s.State != GameSessionState.Ended && s.State != GameSessionState.Cancelled)
                .Where(s => db.Devices.Any(d => (d.SerialPhysical == trimmedCode || d.SerialPhysical == targetSerial) 
                                                && d.MachineDevices.Any(md => md.MachineId == s.MachineId)))
                .OrderByDescending(s => s.StartedAtUtc)
                .FirstOrDefaultAsync();
        }
        
        
        // Return error if no active session is found
        if (session == null)
        {
            return new JoinGameSessionPayload(false, "No active game session found for this pinball machine.", null, null);
        }
        
        // 5. Check if the player is already registered in this session
        var existingPlayer = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (existingPlayer != null)
        {
            return new JoinGameSessionPayload(
                true, 
                $"You are already in this game as Player {existingPlayer.SlotNumber}.", 
                existingPlayer.SlotNumber, 
                session.Machine?.DisplayName
            );
        }
        
        // 6. Find the first open slot (1 to 4)
        var occupiedSlots = session.Players.Select(p => p.SlotNumber).ToHashSet();
        int? freeSlot = null;
        for (int i = 1; i <= 4; i++)
        {
            if (!occupiedSlots.Contains(i))
            {
                freeSlot = i;
                break;
            }
        }
        
        if (!freeSlot.HasValue)
        {
            return new JoinGameSessionPayload(false, "This game session is already full (4 players max).", null, null);
        }
        
        // 7. Execute application command via AddPlayerHandler
        var command = new AddPlayerCommand(session.SessionId, freeSlot.Value, userId);
        var result = await addPlayerHandler.HandleAsync(command);
        if (result is AddPlayerResult.Success success)
        {
            // 8. Notify the physical cabinet in real-time
            lobbyNotifier.NotifyLobbyUpdated(session.SessionId);
            var machineName = session.Machine?.DisplayName ?? "Pinball Machine";
            return new JoinGameSessionPayload(
                true, 
                $"Joined {machineName} as Player {success.SlotNumber}!", 
                success.SlotNumber, 
                machineName
            );
        }
        return new JoinGameSessionPayload(false, "Unable to join session at this moment.", null, null);
    }

}
