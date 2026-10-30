using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WorldPinball.Bff.Logging;

namespace WorldPinball.Bff.Extensions;

public static class RateLimiterExtensions
{
    public static IServiceCollection AddBffRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WorldPinball.Bff.RateLimiting");
                var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                logger.LogWarning(
                    BffLogEvents.RateLimitExceeded,
                    "Rate limit exceeded for IP {ClientIp} on path {Path}",
                    clientIp,
                    context.HttpContext.Request.Path);

                return ValueTask.CompletedTask;
            };

            options.AddPolicy("public-ip-limit", context =>
            {
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: clientIp,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }
}
