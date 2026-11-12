namespace WorldPinball.Application.Security.Cryptography;

/// <summary>
/// Resolves public keys from trusted certificate sources (e.g., mounted X.509 certificates).
/// </summary>
public interface IPublicKeyResolver
{
    /// <summary>
    /// Resolves the public key in SubjectPublicKeyInfo PEM format from the specified certificate file path.
    /// </summary>
    /// <param name="certificatePath">Path to the X.509 certificate (.crt / .pem).</param>
    /// <returns>Public key in SubjectPublicKeyInfo PEM format.</returns>
    string GetPublicKeyPem(string certificatePath);
}
