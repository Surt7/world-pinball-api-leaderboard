using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorldPinball.Application.Security.Cryptography;

namespace WorldPinball.Api.Filters.Security;

public sealed class MachineSignatureFilter(
    IMachineAuthService authService,
    ILogger<MachineSignatureFilter> logger) : IAsyncAuthorizationFilter
{
    public const string DeviceIdItemKey = "AuthenticatedDeviceId";
    
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {   
        var request = context.HttpContext.Request;
        
        
        // 1. Extract security headers
        var deviceId = request.Headers["X-Device-Id"].FirstOrDefault();
        var timestamp = request.Headers["X-Timestamp"].FirstOrDefault();
        var nonce = request.Headers["X-Nonce"].FirstOrDefault();
        var signature = request.Headers["X-Signature"].FirstOrDefault();
        
        // 2. Fail-fast if any header is missing
        if (string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(signature))
        {
            logger.LogWarning("Machine request rejected: missing one or more required security headers.");
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Missing required machine authentication headers (X-Device-Id, X-Timestamp, X-Nonce, X-Signature)."
            });
            return;
        }
        
        // 3. Enable buffering so the stream can be rewound for subsequent Model Binding
        request.EnableBuffering();
        
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
            request.Body.Position = 0; // Rewind the stream to the beginning!
        }
        
        // 4. Build machine authentication context
        var authContext = new MachineAuthContext(
            DeviceIdHeader: deviceId,
            TimestampHeader: timestamp,
            NonceHeader: nonce,
            SignatureHeader: signature,
            HttpMethod: request.Method,
            RequestPath: request.Path.Value ?? string.Empty,
            RawBody: rawBody
        );
        
        // 5. Validate cryptographically via application service
        var authResult = await authService.ValidateAsync(authContext, context.HttpContext.RequestAborted);
        if (authResult is not MachineAuthResult.Success success)
        {
            logger.LogWarning("Machine authentication rejected for Device {DeviceId} on {Path}", deviceId, request.Path.Value);
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Machine authentication failed."
            });
            return;
        }
        
        
        // 6. Store authenticated DeviceId in HttpContext for downstream controller actions
        context.HttpContext.Items[DeviceIdItemKey] = success.DeviceId;
    }
}