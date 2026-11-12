using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using WorldPinball.Application.Security.Cryptography;
using Path = System.IO.Path;

namespace WorldPinball.Infrastructure.Security.Cryptography;

public sealed class EcdsaP256SignatureSigner : ISignatureSigner, IDisposable
{
    
    private readonly ECDsa _ecdsa;

    public EcdsaP256SignatureSigner(IConfiguration configuration)
    {
        var keyPath = configuration["Security:PrivateKeyPath"] ?? "/app/certs/leaderboard.key";
        
        if (!File.Exists(keyPath))
        {
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "leaderboard.key");
            if (File.Exists(localPath)) keyPath = localPath;
        }
        
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"Private key not found: {keyPath}", keyPath);
        }
        var keyPem = File.ReadAllText(keyPath);
        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(keyPem);
        
    }

    
    /// <summary>
    /// Signs canonical byte data using ECDSA over the NIST P-256 curve (SHA-256).
    /// Produces a raw 64-byte IEEE P1363 signature (r || s concatenation) identical to hardware TPM 2.0 output.
    /// </summary>
    public string Sign(byte[] canonicalData)
    {
        ArgumentNullException.ThrowIfNull(canonicalData);
        if (canonicalData.Length == 0)
        {
            throw new ArgumentException("Data cannot be empty.", nameof(canonicalData));
        }
        
        // ECDSA P-256 signature in hardware IEEE P1363 format (64 bytes)
        byte[] rawSig = _ecdsa.SignData(
            canonicalData, 
            HashAlgorithmName.SHA256, 
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        
        // Encode to Base64 for the X-Signature HTTP header
        return Convert.ToBase64String(rawSig);
    }


    public void Dispose()
    {
        _ecdsa.Dispose();
    }
}