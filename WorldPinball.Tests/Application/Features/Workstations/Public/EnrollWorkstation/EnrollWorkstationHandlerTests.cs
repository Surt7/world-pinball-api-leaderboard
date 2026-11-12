using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;
using WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation.commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.CA;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Tests.Application.Features.Workstations.Public.EnrollWorkstation;

public class EnrollWorkstationHandlerTests
{
    private readonly IDeviceEnrollmentReadRepository _readRepo = Substitute.For<IDeviceEnrollmentReadRepository>();
    private readonly IDeviceEnrollmentWriteRepository _writeRepo = Substitute.For<IDeviceEnrollmentWriteRepository>();
    private readonly ICertificateAuthorityService _caService = Substitute.For<ICertificateAuthorityService>();
    private readonly ISecretService _secretService = Substitute.For<ISecretService>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly FakeLogger<EnrollWorkstationHandler> _logger = new();

    private EnrollWorkstationHandler CreateSut() =>
        new(_readRepo, _writeRepo, _caService, _secretService, _timeProvider, _logger);

    [Fact]
    public async Task HandleAsync_WhenTokenIsAdminBootstrap_ShouldIssueAdminWorkstationCertificate()
    {
        // Arrange
        const string plainToken = "admin-secret-token";
        const string tokenHash = "admin-hash";
        const string workstationName = "WP-ADMIN-01";
        const string publicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A...\n-----END PUBLIC KEY-----";

        _secretService.TryHash(plainToken, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = tokenHash;
                return true;
            });

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.AdminBootstrap,
            issuedByUserId: "super-admin",
            issuedAt: DateTime.UtcNow.AddMinutes(-5),
            expiresAt: DateTime.UtcNow.AddHours(2)
        );

        _readRepo.GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var issuedCert = new IssuedWorkstationCertificate(
            CertPem: "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
            SerialNumber: "aabbcc112233",
            WorkstationName: workstationName,
            ThumbprintSha256: "thumbprint123",
            SubjectDn: $"CN={workstationName}, O=WorldPinball, OU=AdminWorkstations",
            IssuerDn: "CN=WorldPinball Device Intermediate CA, O=WorldPinball Internal CA",
            NotBeforeUtc: DateTime.UtcNow.AddMinutes(-5),
            NotAfterUtc: DateTime.UtcNow.AddYears(1)
        );

        _caService.IssueWorkstationCertificateAsync(
            workstationName,
            publicKeyPem,
            WorkstationType.AdminWorkstation,
            Arg.Any<CancellationToken>())
            .Returns(issuedCert);

        var sut = CreateSut();
        var command = new EnrollWorkstationCommand(plainToken, workstationName, publicKeyPem);

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var success = Assert.IsType<EnrollWorkstationResult.Success>(result);
        Assert.Equal("aabbcc112233", success.SerialNumber);
        Assert.Equal("thumbprint123", success.ThumbprintSha256);

        // Token must be burned / consumed
        Assert.NotNull(token.ConsumedAtUtc);
        await _writeRepo.Received(1).UpdateTokenAsync(token, Arg.Any<CancellationToken>());

        // CA service called with AdminWorkstation
        await _caService.Received(1).IssueWorkstationCertificateAsync(
            workstationName,
            publicKeyPem,
            WorkstationType.AdminWorkstation,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTokenIsTerminalBootstrap_ShouldIssueFieldTerminalCertificate()
    {
        // Arrange
        const string plainToken = "terminal-secret-token";
        const string tokenHash = "terminal-hash";
        const string workstationName = "WP-TERM-02";
        const string publicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A...\n-----END PUBLIC KEY-----";

        _secretService.TryHash(plainToken, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = tokenHash;
                return true;
            });

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.TerminalBootstrap,
            issuedByUserId: "admin-1",
            issuedAt: DateTime.UtcNow.AddMinutes(-5),
            expiresAt: DateTime.UtcNow.AddHours(2)
        );

        _readRepo.GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var issuedCert = new IssuedWorkstationCertificate(
            CertPem: "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
            SerialNumber: "term11223344",
            WorkstationName: workstationName,
            ThumbprintSha256: "thumbprintterm",
            SubjectDn: $"CN={workstationName}, O=WorldPinball, OU=FieldTerminals",
            IssuerDn: "CN=WorldPinball Device Intermediate CA, O=WorldPinball Internal CA",
            NotBeforeUtc: DateTime.UtcNow.AddMinutes(-5),
            NotAfterUtc: DateTime.UtcNow.AddYears(1)
        );

        _caService.IssueWorkstationCertificateAsync(
            workstationName,
            publicKeyPem,
            WorkstationType.FieldTerminal,
            Arg.Any<CancellationToken>())
            .Returns(issuedCert);

        var sut = CreateSut();
        var command = new EnrollWorkstationCommand(plainToken, workstationName, publicKeyPem);

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var success = Assert.IsType<EnrollWorkstationResult.Success>(result);
        Assert.Equal("term11223344", success.SerialNumber);

        // CA service called with FieldTerminal
        await _caService.Received(1).IssueWorkstationCertificateAsync(
            workstationName,
            publicKeyPem,
            WorkstationType.FieldTerminal,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTokenTypeIsNotBootstrap_ShouldRejectWithInvalidToken()
    {
        // Arrange
        const string plainToken = "device-token";
        const string tokenHash = "device-hash";

        _secretService.TryHash(plainToken, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = tokenHash;
                return true;
            });

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll, // Not AdminBootstrap or TerminalBootstrap
            issuedByUserId: "admin-1",
            issuedAt: DateTime.UtcNow.AddMinutes(-5),
            expiresAt: DateTime.UtcNow.AddHours(2)
        );

        _readRepo.GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var sut = CreateSut();
        var command = new EnrollWorkstationCommand(plainToken, "WP-ADMIN-01", "public-key");

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var invalid = Assert.IsType<EnrollWorkstationResult.InvalidToken>(result);
        Assert.Contains("not valid for workstation enrollment", invalid.Reason);
        await _caService.DidNotReceive().IssueWorkstationCertificateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkstationType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCaThrowsCryptographicException_ShouldReturnInvalidPublicKey()
    {
        // Arrange
        const string plainToken = "valid-token";
        const string tokenHash = "valid-hash";
        const string workstationName = "WP-TERM-01";
        const string invalidPublicKey = "invalid-corrupted-key";

        _secretService.TryHash(plainToken, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = tokenHash;
                return true;
            });

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.TerminalBootstrap,
            issuedByUserId: "admin-1",
            issuedAt: DateTime.UtcNow.AddMinutes(-5),
            expiresAt: DateTime.UtcNow.AddHours(2)
        );

        _readRepo.GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>()).Returns(token);

        _caService.IssueWorkstationCertificateAsync(
            workstationName,
            invalidPublicKey,
            WorkstationType.FieldTerminal,
            Arg.Any<CancellationToken>())
            .Returns<IssuedWorkstationCertificate>(_ => throw new CryptographicException("Malformed ASN.1 payload."));

        var sut = CreateSut();
        var command = new EnrollWorkstationCommand(plainToken, workstationName, invalidPublicKey);

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var invalidKey = Assert.IsType<EnrollWorkstationResult.InvalidPublicKey>(result);
        Assert.Contains("Malformed ASN.1 payload", invalidKey.Reason);
    }
}
