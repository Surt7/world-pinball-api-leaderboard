using System.Security.Cryptography;
using System.Text;
using WorldPinball.Application.Security.Cryptography;

namespace WorldPinball.Infrastructure.Security.Cryptography;

public sealed class CanonicalBuilder : ICanonicalBuilder
{
    public byte[] Build(long ts, string nonce, string method, string path, string rawBody)
    {
        if (ts <= 0) throw new ArgumentOutOfRangeException(nameof(ts));
        if (string.IsNullOrWhiteSpace(nonce)) throw new ArgumentException("missing nonce", nameof(nonce));
        if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("missing method", nameof(method));

        // Important: must match the client exactly
        var normalizedMethod = method.ToUpperInvariant();
        var normalizedPath = path ?? string.Empty;

        // Body hash (hex lower)
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody ?? string.Empty);
        var bodyHashHexLower = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();

        var canonical = $"{ts}.{nonce}.{normalizedMethod}.{normalizedPath}.{bodyHashHexLower}";
        return Encoding.UTF8.GetBytes(canonical);
    }
}