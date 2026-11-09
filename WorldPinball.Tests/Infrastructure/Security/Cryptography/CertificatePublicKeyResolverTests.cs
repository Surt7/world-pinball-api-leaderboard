using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using WorldPinball.Infrastructure.Security.Cryptography;
using Xunit;

namespace WorldPinball.Tests.Infrastructure.Security.Cryptography;

public sealed class CertificatePublicKeyResolverTests
{
    [Fact]
    public void GetPublicKeyPem_FromValidCertificate_ShouldExtractPublicKeyAndCacheInMemory()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=hub.worldpinball.local", ecdsa, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var tempCertPath = Path.Combine(Path.GetTempPath(), $"test-cert-{Guid.NewGuid()}.crt");
        File.WriteAllText(tempCertPath, cert.ExportCertificatePem());

        var resolver = new CertificatePublicKeyResolver();
        var expectedPublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        try
        {
            // Act: First call loads and caches
            var key1 = resolver.GetPublicKeyPem(tempCertPath);

            // Assert
            key1.Should().Be(expectedPublicKeyPem);

            // Act: Second call should retrieve from in-memory cache even if file is removed
            File.Delete(tempCertPath);
            var key2 = resolver.GetPublicKeyPem(tempCertPath);

            key2.Should().Be(expectedPublicKeyPem);
        }
        finally
        {
            if (File.Exists(tempCertPath))
            {
                File.Delete(tempCertPath);
            }
        }
    }

    [Fact]
    public void GetPublicKeyPem_WhenFileNotFound_ShouldThrowFileNotFoundException()
    {
        var resolver = new CertificatePublicKeyResolver();
        var act = () => resolver.GetPublicKeyPem("/non/existent/path/cert.crt");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Verify_SignatureFromHub_WithRealStepCaCertificates_ShouldSucceed()
    {
        // Arrange - Check for the real Step-CA issued hub.crt & hub.key in docker directory
        var certPath = Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "hub.crt");
        var keyPath = Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "hub.key");

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return;
        }

        var config = Substitute.For<IConfiguration>();
        config["Security:PrivateKeyPath"].Returns(keyPath);

        // 1. Simulating Hub signing a message to Leaderboard
        using var hubSigner = new EcdsaP256SignatureSigner(config);
        byte[] payload = Encoding.UTF8.GetBytes("{\"action\":\"sync_user_profile\",\"userId\":\"wp-user-77\",\"timestamp\":1741560000}");
        string hubSignature = hubSigner.Sign(payload);

        // 2. Leaderboard receives the message, resolves Hub's public key from the mounted hub.crt
        var resolver = new CertificatePublicKeyResolver();
        string hubPublicKeyPem = resolver.GetPublicKeyPem(certPath);

        // 3. Leaderboard verifies the signature with its EcdsaP256SignatureVerifier
        var verifier = new EcdsaP256SignatureVerifier();
        bool isValid = verifier.Verify(hubPublicKeyPem, payload, hubSignature);

        // Assert - Leaderboard accepts Hub's signature with 100% mathematical certainty
        isValid.Should().BeTrue();
    }
}
