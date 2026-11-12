using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Dtos;
using WorldPinball.Application.Features.Machines.Admin.GetMachineSecurity.Queries;
using WorldPinball.Application.Interfaces.Repositories;

namespace WorldPinball.Tests.Application.Features.Machines.Admin.GetMachineSecurity;

public sealed class GetMachineSecurityHandlerTests
{
    private readonly IMachineReadRepository _readRepository = Substitute.For<IMachineReadRepository>();
    private readonly ILogger<GetMachineSecurityHandler> _logger = Substitute.For<ILogger<GetMachineSecurityHandler>>();
    private readonly GetMachineSecurityHandler _sut;

    public GetMachineSecurityHandlerTests()
    {
        _sut = new GetMachineSecurityHandler(_readRepository, _logger);
    }

    [Fact]
    public async Task HandleAsync_WhenMachineDoesNotExist_ShouldReturnMachineNotFound()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        _readRepository.GetMachineSecurityAsync(machineId, Arg.Any<CancellationToken>())
            .Returns((MachineSecurityDto?)null);

        // Act
        var result = await _sut.HandleAsync(new GetMachineSecurityQuery(machineId), CancellationToken.None);

        // Assert
        var notFound = result.Should().BeOfType<GetMachineSecurityResult.MachineNotFound>().Which;
        notFound.MachineId.Should().Be(machineId);
    }

    [Fact]
    public async Task HandleAsync_WhenMachineExistsWithoutDevice_ShouldReturnSuccessWithNoAttachedDevice()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var securityDto = new MachineSecurityDto(
            machineId,
            "WP-TEST-001",
            "Medieval Madness Test",
            1, // Active
            HasAttachedDevice: false,
            DeviceId: null,
            DevicePhysicalSerial: null,
            HasCertificate: false,
            CertSerialNumber: null,
            CertThumbprintSha256: null,
            SubjectDn: null,
            IssuerDn: null,
            NotBeforeUtc: null,
            NotAfterUtc: null,
            CertStatus: null,
            IsExpired: false,
            IsValid: false,
            StatusExplanation: "Aucun périphérique TPM n'est associé à cette machine."
        );

        _readRepository.GetMachineSecurityAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(securityDto);

        // Act
        var result = await _sut.HandleAsync(new GetMachineSecurityQuery(machineId), CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetMachineSecurityResult.Success>().Which;
        success.Security.Should().Be(securityDto);
        success.Security.HasAttachedDevice.Should().BeFalse();
        success.Security.HasCertificate.Should().BeFalse();
        success.Security.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenMachineHasValidCertificate_ShouldReturnSuccessWithValidStatus()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var securityDto = new MachineSecurityDto(
            machineId,
            "WP-TEST-002",
            "Attack From Mars Test",
            1, // Active
            HasAttachedDevice: true,
            DeviceId: deviceId,
            DevicePhysicalSerial: "TPM-DEV-9999",
            HasCertificate: true,
            CertSerialNumber: "1234567890ABCDEF",
            CertThumbprintSha256: "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            SubjectDn: "CN=TPM-DEV-9999, O=WorldPinball, C=FR",
            IssuerDn: "CN=WorldPinball Intermediate CA, O=WorldPinball, C=FR",
            NotBeforeUtc: DateTime.UtcNow.AddDays(-30),
            NotAfterUtc: DateTime.UtcNow.AddDays(335),
            CertStatus: 1, // Active
            IsExpired: false,
            IsValid: true,
            StatusExplanation: "Certificat matériel TPM valide et vérifié. Liaison mTLS opérationnelle."
        );

        _readRepository.GetMachineSecurityAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(securityDto);

        // Act
        var result = await _sut.HandleAsync(new GetMachineSecurityQuery(machineId), CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetMachineSecurityResult.Success>().Which;
        success.Security.Should().Be(securityDto);
        success.Security.HasAttachedDevice.Should().BeTrue();
        success.Security.HasCertificate.Should().BeTrue();
        success.Security.IsValid.Should().BeTrue();
        success.Security.IsExpired.Should().BeFalse();
        success.Security.DevicePhysicalSerial.Should().Be("TPM-DEV-9999");
    }

    [Fact]
    public async Task HandleAsync_WhenMachineHasExpiredCertificate_ShouldReturnSuccessWithIsExpiredTrue()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var securityDto = new MachineSecurityDto(
            machineId,
            "WP-TEST-003",
            "Monster Bash Test",
            1, // Active
            HasAttachedDevice: true,
            DeviceId: deviceId,
            DevicePhysicalSerial: "TPM-DEV-EXPIRED",
            HasCertificate: true,
            CertSerialNumber: "EXPIRED123456789",
            CertThumbprintSha256: "AAAAFFFF0000111122223333444455556666777788889999AAAABBBBCCCCDDDD",
            SubjectDn: "CN=TPM-DEV-EXPIRED, O=WorldPinball, C=FR",
            IssuerDn: "CN=WorldPinball Intermediate CA, O=WorldPinball, C=FR",
            NotBeforeUtc: DateTime.UtcNow.AddYears(-2),
            NotAfterUtc: DateTime.UtcNow.AddDays(-10),
            CertStatus: 1, // Active in DB but expired by date
            IsExpired: true,
            IsValid: false,
            StatusExplanation: "Certificat matériel expiré."
        );

        _readRepository.GetMachineSecurityAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(securityDto);

        // Act
        var result = await _sut.HandleAsync(new GetMachineSecurityQuery(machineId), CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetMachineSecurityResult.Success>().Which;
        success.Security.IsExpired.Should().BeTrue();
        success.Security.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenMachineHasRevokedCertificate_ShouldReturnSuccessWithRevokedStatus()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var securityDto = new MachineSecurityDto(
            machineId,
            "WP-TEST-004",
            "Twilight Zone Test",
            2, // Revoked
            HasAttachedDevice: true,
            DeviceId: deviceId,
            DevicePhysicalSerial: "TPM-DEV-REVOKED",
            HasCertificate: true,
            CertSerialNumber: "REVOKED123456789",
            CertThumbprintSha256: "DEADBEEF0000111122223333444455556666777788889999AAAABBBBCCCCDDDD",
            SubjectDn: "CN=TPM-DEV-REVOKED, O=WorldPinball, C=FR",
            IssuerDn: "CN=WorldPinball Intermediate CA, O=WorldPinball, C=FR",
            NotBeforeUtc: DateTime.UtcNow.AddDays(-100),
            NotAfterUtc: DateTime.UtcNow.AddDays(265),
            CertStatus: 2, // Revoked
            IsExpired: false,
            IsValid: false,
            StatusExplanation: "Certificat révoqué."
        );

        _readRepository.GetMachineSecurityAsync(machineId, Arg.Any<CancellationToken>())
            .Returns(securityDto);

        // Act
        var result = await _sut.HandleAsync(new GetMachineSecurityQuery(machineId), CancellationToken.None);

        // Assert
        var success = result.Should().BeOfType<GetMachineSecurityResult.Success>().Which;
        success.Security.CertStatus.Should().Be(2);
        success.Security.IsValid.Should().BeFalse();
    }
}
