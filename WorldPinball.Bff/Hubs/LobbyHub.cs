using Microsoft.AspNetCore.SignalR;
using WorldPinball.Bff.Services;
namespace WorldPinball.Bff.Hubs;

public sealed class LobbyHub(ILobbyRelayService relayService) : Hub
{
    public async Task JoinLobby(string sessionId)
    {

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        if (Guid.TryParse(sessionId, out var parsedId))
        {
            await relayService.EnsureWatchingLobbyAsync(parsedId, CancellationToken.None);
            if (relayService.GetCurrentLobbies().FirstOrDefault(l => l.SessionId == sessionId) is { } cached)
            {
                await Clients.Caller.SendAsync("LobbyStateUpdated", cached);
            }
        }
    }

    public async Task LeaveLobby(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
    
    
    public async Task JoinAdminSupervision()
    {
       
        if (Context.User?.IsInRole("Admin") != true)
        {
            throw new HubException("Forbidden: Only Administrators can join supervision.");
        }
     
        await Groups.AddToGroupAsync(Context.ConnectionId, "AdminSupervision");
        await relayService.EnsureWatchingAllLobbiesAsync(CancellationToken.None);
        
        foreach (var lobby in relayService.GetCurrentLobbies())
        {
            await Clients.Caller.SendAsync("LobbyStateUpdated", lobby);
        }
    }
    public async Task LeaveAdminSupervision()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AdminSupervision");
    }
    
    
    
}