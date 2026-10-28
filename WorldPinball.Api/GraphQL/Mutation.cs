using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;
using WorldPinball.Infrastructure.Persistence;

namespace WorldPinball.Api.GraphQL;

public record ClaimMachinePayload(bool Success, string Message, Machine? Machine);

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
}
