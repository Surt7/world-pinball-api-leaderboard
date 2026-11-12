using Microsoft.AspNetCore.Http;
using WorldPinball.Api.Filters.Security;

namespace WorldPinball.Api.Extensions;

public static class HttpContextSecurityExtensions
{
    
    public static Guid GetAuthenticatedDeviceId(this HttpContext context)
    {
        if (!context.Items.TryGetValue(MachineSignatureFilter.DeviceIdItemKey, out var deviceIdObj))
            throw new InvalidOperationException(
                "Authenticated device ID is missing from HttpContext. " +
                "Ensure the endpoint or controller is decorated with [RequireMachineSignature].");
        
        return deviceIdObj switch
        {
            Guid guid when guid != Guid.Empty => guid,
            string str when Guid.TryParse(str, out var parsedGuid) && parsedGuid != Guid.Empty => parsedGuid,
            _ => throw new InvalidOperationException("Authenticated device ID is missing from HttpContext. " +
                                                     "Ensure the endpoint or controller is decorated with [RequireMachineSignature].")
        };
    }
}