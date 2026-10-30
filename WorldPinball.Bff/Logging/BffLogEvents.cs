using Microsoft.Extensions.Logging;

namespace WorldPinball.Bff.Logging;

public static class BffLogEvents
{
    // 1000 - 1099: Reverse Proxy / YARP Forwarder events
    public static readonly EventId ProxyForwardError = new(1001, nameof(ProxyForwardError));
    public static readonly EventId ProxyDestinationUnavailable = new(1002, nameof(ProxyDestinationUnavailable));

    // 1100 - 1199: Rate Limiting & Throttling events
    public static readonly EventId RateLimitExceeded = new(1101, nameof(RateLimitExceeded));

    // 1200 - 1299: Authentication, OIDC & Token events
    public static readonly EventId OidcAuthenticationFailed = new(1201, nameof(OidcAuthenticationFailed));
    public static readonly EventId OidcRemoteFailure = new(1202, nameof(OidcRemoteFailure));
    public static readonly EventId MissingBearerToken = new(1203, nameof(MissingBearerToken));

    // 1300 - 1399: gRPC & SignalR Relay events
    public static readonly EventId LobbyRelayStreamError = new(1301, nameof(LobbyRelayStreamError));
    public static readonly EventId LobbyRelayAllStreamError = new(1302, nameof(LobbyRelayAllStreamError));
}
