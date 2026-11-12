using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using WorldPinball.Infrastructure.Security.Cryptography;
using Xunit;
using System.Security.Cryptography.X509Certificates;

namespace WorldPinball.Tests.Infrastructure.Security.Cryptography;

public sealed class EcdsaP256SignatureSignerTests : IDisposable
{
    private readonly string _tempKeyPath;
    private readonly string _publicKeyPem;
    private readonly IConfiguration _configuration;

    public EcdsaP256SignatureSignerTests()
    {
        // Generate an ephemeral ECDSA P-256 key pair for the test
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        // Write the private key to a temporary file
        _tempKeyPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.key");
        File.WriteAllText(_tempKeyPath, ecdsa.ExportECPrivateKeyPem());

        // Mock IConfiguration to point to our temporary private key
        _configuration = Substitute.For<IConfiguration>();
        _configuration["Security:PrivateKeyPath"].Returns(_tempKeyPath);
    }

    public void Dispose()
    {
        // Clean up the temporary file after tests execute
        if (File.Exists(_tempKeyPath))
        {
            File.Delete(_tempKeyPath);
        }
    }
    
    
    [Fact]
    public void Sign_WithValidData_ShouldProduce64ByteSignatureAndBeVerifiable()
    {
        // Arrange
        using var signer = new EcdsaP256SignatureSigner(_configuration);
        var verifier = new EcdsaP256SignatureVerifier();
        byte[] payload = Encoding.UTF8.GetBytes("{\"gameId\":\"godzilla\",\"score\":42000000}");

        // Act
        string signatureBase64 = signer.Sign(payload);

        // Assert - Verify IEEE P1363 64-byte raw format (TPM standard)
        signatureBase64.Should().NotBeNullOrWhiteSpace();
        byte[] rawSig = Convert.FromBase64String(signatureBase64);
        rawSig.Should().HaveCount(64);

        // Assert - Verify cryptographic validity with the public key
        bool isValid = verifier.Verify(_publicKeyPem, payload, signatureBase64);
        isValid.Should().BeTrue();
    }
    
    [Fact]
    public void Sign_WhenDataIsTampered_ShouldFailVerification()
    {
        // Arrange
        using var signer = new EcdsaP256SignatureSigner(_configuration);
        var verifier = new EcdsaP256SignatureVerifier();
        byte[] originalPayload = Encoding.UTF8.GetBytes("{\"gameId\":\"godzilla\",\"score\":42000000}");
        byte[] tamperedPayload = Encoding.UTF8.GetBytes("{\"gameId\":\"godzilla\",\"score\":99999999}");
        // Act
        string signatureBase64 = signer.Sign(originalPayload);
        // Assert - Verifier must reject tampered payload
        bool isValid = verifier.Verify(_publicKeyPem, tamperedPayload, signatureBase64);
        isValid.Should().BeFalse();
    }
    [Fact]
    public void Sign_WithNullOrEmptyData_ShouldThrowException()
    {
        // Arrange
        using var signer = new EcdsaP256SignatureSigner(_configuration);
        // Act & Assert
        var actNull = () => signer.Sign(null!);
        actNull.Should().Throw<ArgumentNullException>();
        var actEmpty = () => signer.Sign([]);
        actEmpty.Should().Throw<ArgumentException>()
            .WithMessage("*Data cannot be empty*");
    }
    [Fact]
    public void Constructor_WhenKeyFileNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingConfig = Substitute.For<IConfiguration>();
        missingConfig["Security:PrivateKeyPath"].Returns("/non/existent/path/leaderboard.key");
        // Act
        var act = () => new EcdsaP256SignatureSigner(missingConfig);
        // Assert
        act.Should().Throw<FileNotFoundException>();
    }
    
    
    [Fact]
    public void Sign_WithRealStepCaCertificates_ShouldVerifyAgainstX509PublicCert()
    {
        // Arrange - Locate the real Step-CA issued cert & key from docker directory
        var certPath = Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "leaderboard.crt");
        var keyPath = Path.Combine(Directory.GetCurrentDirectory(), "docker", "nginx", "certs", "leaderboard.key");

        // Skip gracefully if run in an environment without the mounted docker certs
        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return;
        }

        var config = Substitute.For<IConfiguration>();
        config["Security:PrivateKeyPath"].Returns(keyPath);

        using var signer = new EcdsaP256SignatureSigner(config);
        var verifier = new EcdsaP256SignatureVerifier();
        byte[] payload = Encoding.UTF8.GetBytes("{\"event\":\"score_submitted\",\"gameId\":\"twilight-zone\",\"score\":999000}");

        // Act - Leaderboard signs payload with private key
        string signature = signer.Sign(payload);

        // Load the public X.509 certificate (simulating Hub's CertificatePublicKeyResolver)
        using var cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
        using var ecdsaPublic = cert.GetECDsaPublicKey()!;
        string publicKeyPem = ecdsaPublic.ExportSubjectPublicKeyInfoPem();

        // Assert - Hub verifies the signature using Leaderboard's certificate public key
        bool isValid = verifier.Verify(publicKeyPem, payload, signature);
        isValid.Should().BeTrue();
    }
}