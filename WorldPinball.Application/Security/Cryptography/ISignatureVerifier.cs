namespace WorldPinball.Application.Security.Cryptography;

public interface ISignatureVerifier
{
    bool Verify(string publicKeyPem, byte[] data, string signatureBase64, string? algorithm = null);
}