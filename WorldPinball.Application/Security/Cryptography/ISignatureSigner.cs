namespace WorldPinball.Application.Security.Cryptography;



/// <summary>
/// Cryptographic signature generator used to sign canonical byte payloads using an ECDSA P-256 private key.
/// Produces a raw 64-byte IEEE P1363 signature identical to hardware TPM 2.0 output.
/// </summary>
public interface ISignatureSigner
{
    /// <summary>
    /// Signs canonical byte data and returns the Base64-encoded IEEE P1363 signature.
    /// </summary>
    string Sign(byte[] canonicalData);
}