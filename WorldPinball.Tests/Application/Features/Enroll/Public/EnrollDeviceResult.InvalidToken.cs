using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WorldPinball.Application.Features.Machines.Public;
using WorldPinball.Application.Features.Machines.Public.Commands;
using WorldPinball.Application.Features.Machines.Public.Enroll;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.CA;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;
using WorldPinball.Infrastructure.Security.Secrets;

namespace WorldPinball.Tests.Application.Features.Enroll.Public;

public class EnrollDeviceResultInvalidToken
{
    private readonly ISecretService _secretService = new SecretService(new EphemeralDataProtectionProvider());

    private static (string PlainToken, string TokenHash) GenerateValidTokenPair()
    {
        byte[] rawBytes = new byte[32];
        RandomNumberGenerator.Fill(rawBytes);
        string plainToken = Convert.ToBase64String(rawBytes);
        string tokenHash = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        return (plainToken, tokenHash);
    }

    [Fact]
    public async Task HandleAsync_WhenMachineIdIsEmpty_ShouldReturnMachineNotFound()
    {
        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var command = new EnrollDeviceCommand(
            MachineId: Guid.Empty, // <--- EMPTY GUID
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: "some-plain-token",
            PublicKeyPem: "PUBLIC_KEY"
        );

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            TimeProvider.System
        );
        
        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.MachineNotFound>();
        var notFoundResult = (EnrollDeviceResult.MachineNotFound)result;
        notFoundResult.MachineId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task HandleAsync_WhenMachineIdMismatch_ShouldReturnMachineNotFound()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var tokenMachineId = Guid.NewGuid();
        var commandMachineId = Guid.NewGuid(); // DIFFERENTS !

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: tokenMachineId // <--- Lié à tokenMachineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        var command = new EnrollDeviceCommand(
            MachineId: commandMachineId, // <--- Requis avec commandMachineId
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );
        
        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.MachineNotFound>();
        var notFoundResult = (EnrollDeviceResult.MachineNotFound)result;
        notFoundResult.MachineId.Should().Be(commandMachineId);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenFormatIsInvalid_ShouldReturnInvalidToken()
    {
        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var command = new EnrollDeviceCommand(
            MachineId: Guid.NewGuid(),
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: "not-a-valid-base64-token",
            PublicKeyPem: "PUBLIC_KEY"
        );

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            TimeProvider.System
        );
        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.InvalidToken>();
    }

    [Fact]
    public async Task HandleAsync_WhenTokenDoesNotExistInRepository_ShouldReturnInvalidToken()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns((EnrollmentToken?)null);

        var command = new EnrollDeviceCommand(
            MachineId: Guid.NewGuid(),
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            TimeProvider.System
        );
        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.InvalidToken>();
    }

    [Fact]
    public async Task HandleAsync_WhenDeviceSerialAlreadyExistsInRepository_ShouldReturnDeviceAlreadyEnrolled()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var machineId = Guid.NewGuid(); // <-- Set common ID

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: machineId // <-- Bind ID
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-12345", Arg.Any<CancellationToken>())
            .Returns(true);

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.DeviceAlreadyEnrolled>();
    }

    [Fact]
    public async Task HandleAsync_WhenTokenAndMachineAreValid_ShouldReturnSuccess()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var machine = new Machine(
            ipId: Guid.NewGuid(),
            serialNumber: "WP-TEST-001",
            displayName: "Test Machine",
            registeredAtUtc: now,
            idempotencyKey: Guid.NewGuid(),
            nonce: "test-nonce"
        );
        var machineId = machine.MachineId;

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: machineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-12345", Arg.Any<CancellationToken>())
            .Returns(false);

        repoMachineWrite
            .GetByIdAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(machine);

        caService.IssueDeviceCertificateAsync(Arg.Any<Guid>(),
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<CancellationToken>())
            .Returns(new IssuedDeviceCertificate(
                CertPem: "-----BEGIN CERTIFICATE-----\nTEST_CERT\n-----END CERTIFICATE-----",
                SerialNumber: "cert-serial-123",
                MachineId: machineId,
                ThumbprintSha256: "thumbprint-123",
                SubjectDn: "CN=DEV-12345",
                IssuerDn: "CN=WorldPinball Intermediate CA",
                NotBeforeUtc: now,
                NotAfterUtc: now.AddYears(1)));

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.Success>();
        token.UsesCount.Should().Be(1);
        machine.Status.Should().Be(MachineStatus.Active);

        await repoDeviceWriteEnroll.Received(1).SaveEnrollmentAsync(
            Arg.Any<Device>(),
            Arg.Any<MachineDevice>(),
            token,
            Arg.Any<CancellationToken>()
        );

        await repoDeviceWriteEnroll.Received(1).SaveCertificateAsync(
            Arg.Is<DeviceCertificate>(c => c.CertSerialNumber == "cert-serial-123"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task HandleAsync_WhenEnrollmentSucceeds_ShouldSaveDeviceCertificateWithIntermediateCa()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();
        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();

        var machine = new Machine(
            ipId: Guid.NewGuid(),
            serialNumber: "WP-TEST-CERT",
            displayName: "Test Machine For Cert",
            registeredAtUtc: now,
            idempotencyKey: Guid.NewGuid(),
            nonce: "test-nonce"
        );
        var machineId = machine.MachineId;

        var ca = new CaCertificate(
            CaType.Intermediate,
            "CN=WorldPinball Development CA 2",
            "CN=WorldPinball Development CA 1",
            "CA-0002",
            "thumbprint-ca",
            "PEM-CA",
            now.AddDays(-30),
            now.AddYears(5),
            now
        );

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: machineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-CERT-01", Arg.Any<CancellationToken>())
            .Returns(false);

        repoDeviceReadEnroll
            .GetActiveCaAsync(Arg.Any<CancellationToken>())
            .Returns(ca);

        repoMachineWrite
            .GetByIdAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(machine);

        caService.IssueDeviceCertificateAsync(Arg.Any<Guid>(),
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<CancellationToken>())
            .Returns(new IssuedDeviceCertificate(
                CertPem: "-----BEGIN CERTIFICATE-----\nTEST_CERT\n-----END CERTIFICATE-----",
                SerialNumber: "cert-persisted-serial",
                MachineId: machineId,
                ThumbprintSha256: "thumbprint-cert-persisted",
                SubjectDn: "CN=DEV-CERT-01",
                IssuerDn: "CN=WorldPinball Development CA 2",
                NotBeforeUtc: now,
                NotAfterUtc: now.AddYears(1)));

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-CERT-01",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.Success>();
        await repoDeviceWriteEnroll.Received(1).SaveCertificateAsync(
            Arg.Is<DeviceCertificate>(c => 
                c.CertSerialNumber == "cert-persisted-serial" && 
                c.CaId == ca.CaId &&
                c.Status == DeviceCertificateStatus.Active),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task HandleAsync_WhenTokenIsExpired_ShouldReturnInvalidToken()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var machineId = Guid.NewGuid();

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now.AddHours(-2),
            expiresAt: now.AddHours(-1),
            machineId: machineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-12345", Arg.Any<CancellationToken>())
            .Returns(false);

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.InvalidToken>();
        token.UsesCount.Should().Be(0);

        await repoDeviceWriteEnroll.DidNotReceive().SaveEnrollmentAsync(
            Arg.Any<Device>(),
            Arg.Any<MachineDevice>(),
            Arg.Any<EnrollmentToken>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task HandleAsync_WhenMachineDoesNotExistInRepository_ShouldReturnMachineNotFound()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var machineId = Guid.NewGuid();

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: machineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-12345", Arg.Any<CancellationToken>())
            .Returns(false);

        repoMachineWrite
            .GetByIdAsync(machineId, Arg.Any<CancellationToken>())
            .Returns((Machine?)null);

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.MachineNotFound>();
        var notFound = (EnrollDeviceResult.MachineNotFound)result;
        notFound.MachineId.Should().Be(machineId);

        await repoDeviceWriteEnroll.DidNotReceive().SaveEnrollmentAsync(
            Arg.Any<Device>(),
            Arg.Any<MachineDevice>(),
            Arg.Any<EnrollmentToken>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task HandleAsync_WhenMachineCannotCompleteEnrollment_ShouldReturnInvalidToken()
    {
        var (plainText, tokenHash) = GenerateValidTokenPair();
        var timeProvider = TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var logger = NullLogger<EnrollDeviceHandler>.Instance;
        var repoDeviceReadEnroll = Substitute.For<IDeviceEnrollmentReadRepository>();
        var repoDeviceWriteEnroll = Substitute.For<IDeviceEnrollmentWriteRepository>();
        var repoMachineWrite = Substitute.For<IMachineWriteRepository>();
        var caService = Substitute.For<ICertificateAuthorityService>();

        var machine = new Machine(
            ipId: Guid.NewGuid(),
            serialNumber: "WP-TEST-002",
            displayName: "Test Machine",
            registeredAtUtc: now,
            idempotencyKey: Guid.NewGuid(),
            nonce: "test-nonce"
        );
        machine.CompleteEnrollment();

        var machineId = machine.MachineId;

        var token = new EnrollmentToken(
            tokenHash: tokenHash,
            tokenType: EnrollmentTokenType.DeviceEnroll,
            issuedByUserId: "admin-1",
            issuedAt: now,
            expiresAt: now.AddHours(1),
            machineId: machineId
        );

        repoDeviceReadEnroll
            .GetTokenByHashAsync(tokenHash, Arg.Any<CancellationToken>())
            .Returns(token);

        repoDeviceReadEnroll
            .DeviceSerialExistsAsync("DEV-12345", Arg.Any<CancellationToken>())
            .Returns(false);

        repoMachineWrite
            .GetByIdAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(machine);

        var command = new EnrollDeviceCommand(
            MachineId: machineId,
            DeviceSerialNumber: "DEV-12345",
            PlainEnrollmentToken: plainText,
            PublicKeyPem: "PUBLIC_KEY"
        );

        var sut = new EnrollDeviceHandler(
            repoDeviceWriteEnroll,
            repoDeviceReadEnroll,
            repoMachineWrite,
            caService,
            _secretService,
            logger,
            timeProvider
        );

        var result = await sut.HandleAsync(command);

        result.Should().BeOfType<EnrollDeviceResult.InvalidToken>();

        await repoDeviceWriteEnroll.DidNotReceive().SaveEnrollmentAsync(
            Arg.Any<Device>(),
            Arg.Any<MachineDevice>(),
            Arg.Any<EnrollmentToken>(),
            Arg.Any<CancellationToken>()
        );
    }
}
