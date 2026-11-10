
namespace WorldPinball.Application.Security.Cryptography;

public interface ICanonicalBuilder
{
    byte[] Build(long ts, string nonce, string method, string path, string rawBody);
}