using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using WorldPinball.Application.Security.Cryptography;

namespace WorldPinball.Infrastructure.Security.Cryptography;

/// <summary>
/// High-performance resolver that extracts and caches public keys directly from X.509 certificates.
/// Avoids storing raw PEM strings in configuration files and supports both ECDSA and RSA public keys.
/// </summary>
public sealed class CertificatePublicKeyResolver : IPublicKeyResolver
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string GetPublicKeyPem(string certificatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);

        return _cache.GetOrAdd(certificatePath, path =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Certificate file not found at: {path}", path);
            }

            using var cert = X509CertificateLoader.LoadCertificateFromFile(path);

            // 1. Try ECDSA first (Step-CA standard in WorldPinball)
            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa is not null)
            {
                return ecdsa.ExportSubjectPublicKeyInfoPem();
            }

            // 2. Fallback to RSA if standard web certificate
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null)
            {
                return rsa.ExportSubjectPublicKeyInfoPem();
            }

            throw new InvalidOperationException($"The certificate at '{path}' does not contain a supported public key (ECDSA or RSA).");
        });
    }
}
