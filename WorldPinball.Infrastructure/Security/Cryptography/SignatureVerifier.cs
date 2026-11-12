
using System.Security.Cryptography;
using WorldPinball.Application.Security.Cryptography;

namespace WorldPinball.Infrastructure.Security.Cryptography
{
    /// <summary>
/// High-performance, zero-allocation cryptographic signature verifier using ECDSA over the NIST P-256 curve (SHA-256).
/// Employs <see cref="Span{T}"/> and <c>stackalloc</c> to parse Base64 signatures entirely on the stack, eliminating Heap allocations
/// and reducing Garbage Collection pressure under high packet ingestion.
/// Automatically detects and supports both raw IEEE P1363 (64-byte r||s concatenation typical of hardware TPM/microcontrollers)
/// and RFC 3279 ASN.1 DER sequence formats emitted by standard OpenSSL toolchains.
/// </summary>
public sealed class EcdsaP256SignatureVerifier : ISignatureVerifier
    {

        private const string AllowedAlg = "ecdsa-p256-sha256";

        public bool Verify(string publicKeyPem, byte[] data, string signatureBase64, string? algorithm = null)
     => VerifyEcdsaP256Sha256(publicKeyPem, data, signatureBase64, algorithm);

        /// <summary>
        /// Verify an ECDSA P-256 signature over the provided data (canonical bytes).
        /// - publicKeyPem: PEM "BEGIN PUBLIC KEY" (SubjectPublicKeyInfo)
        /// - signatureBase64: signature bytes encoded in base64. Can be DER or RAW (r||s) 64 bytes.
        /// </summary>
        private bool VerifyEcdsaP256Sha256(string publicKeyPem, byte[] data, string signatureBase64, string? algorithm = null)
        {


            if (!string.IsNullOrWhiteSpace(algorithm) &&
                !algorithm.Equals(AllowedAlg, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Algorithm not supported: {algorithm}");
            if (string.IsNullOrWhiteSpace(publicKeyPem))
                throw new ArgumentException("Public key PEM is missing.", nameof(publicKeyPem));

            if (data is null || data.Length == 0)
                throw new ArgumentException("Data is missing.", nameof(data));

            if (string.IsNullOrWhiteSpace(signatureBase64))
                throw new ArgumentException("Signature is missing.", nameof(signatureBase64));

            Span<byte> sigBytes = stackalloc byte[128];

            if (!Convert.TryFromBase64String(signatureBase64, sigBytes, out int bytesWritten))
            {
                throw new UnauthorizedAccessException("Invalid X-Signature (base64).");
            }

            var actualSig = sigBytes[..bytesWritten];

            var format = bytesWritten == 64
                ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                : DSASignatureFormat.Rfc3279DerSequence;

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);

                return ecdsa.VerifyData(data, actualSig, HashAlgorithmName.SHA256, format);
            }
            catch (CryptographicException ex)
            {
                // Wrong key format, wrong curve, invalid DER, etc.
                throw new UnauthorizedAccessException($"Signature verification failed: {ex.Message}");
            }
        }
    }
}
