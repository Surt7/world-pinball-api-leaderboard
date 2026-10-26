using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;
using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Security.Secrets;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;

namespace WorldPinball.Tests.Application.Features.Workstations.Admin.CreateBootstrapToken;

public class CreateBootstrapTokenHandlerTests
{
    private readonly IDeviceEnrollmentWriteRepository _enrollmentRepo = Substitute.For<IDeviceEnrollmentWriteRepository>();
    private readonly ISecretService _secretService = Substitute.For<ISecretService>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly FakeLogger<CreateBootstrapTokenHandler> _logger = new();

    private CreateBootstrapTokenHandler CreateSut() =>
        new(_enrollmentRepo, _secretService, _timeProvider, _logger);

    [Fact]
    public async Task HandleAsync_WhenAdminWorkstationRequested_ShouldCreateAdminBootstrapToken()
    {
        // Arrange
        _secretService.Generate().Returns(new SecretGenerationResult("plain-secret", "hash-secret", "encrypted-secret"));

        var sut = CreateSut();
        var command = new CreateBootstrapTokenCommand(
            IssuedByUserId: "admin-1",
            WorkstationType: WorkstationType.AdminWorkstation,
            Notes: "Admin console for Alice",
            ValidityHours: 4
        );

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var success = Assert.IsType<CreateBootstrapTokenResult.Success>(result);
        Assert.Equal("plain-secret", success.RawBootstrapToken);
        Assert.Equal(WorkstationType.AdminWorkstation, success.WorkstationType);

        await _enrollmentRepo.Received(1).AddTokenAsync(
            Arg.Is<EnrollmentToken>(t =>
                t.TokenType == EnrollmentTokenType.AdminBootstrap &&
                t.IssuedByUserId == "admin-1" &&
                t.Notes == "Admin console for Alice" &&
                t.MaxUses == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenFieldTerminalRequested_ShouldCreateTerminalBootstrapToken()
    {
        // Arrange
        _secretService.Generate().Returns(new SecretGenerationResult("plain-terminal-secret", "hash-terminal-secret", "encrypted-secret"));

        var sut = CreateSut();
        var command = new CreateBootstrapTokenCommand(
            IssuedByUserId: "admin-1",
            WorkstationType: WorkstationType.FieldTerminal,
            Notes: "Qt terminal on floor 2",
            ValidityHours: 2
        );

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        var success = Assert.IsType<CreateBootstrapTokenResult.Success>(result);
        Assert.Equal("plain-terminal-secret", success.RawBootstrapToken);
        Assert.Equal(WorkstationType.FieldTerminal, success.WorkstationType);

        await _enrollmentRepo.Received(1).AddTokenAsync(
            Arg.Is<EnrollmentToken>(t =>
                t.TokenType == EnrollmentTokenType.TerminalBootstrap &&
                t.IssuedByUserId == "admin-1" &&
                t.Notes == "Qt terminal on floor 2" &&
                t.MaxUses == 1),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("   ", 2)]
    [InlineData("admin-1", 0)]
    [InlineData("admin-1", -1)]
    public async Task HandleAsync_WhenInvalidInputs_ShouldReturnFailure(string userId, int validityHours)
    {
        // Arrange
        var sut = CreateSut();
        var command = new CreateBootstrapTokenCommand(
            IssuedByUserId: userId,
            WorkstationType: WorkstationType.FieldTerminal,
            ValidityHours: validityHours
        );

        // Act
        var result = await sut.HandleAsync(command);

        // Assert
        Assert.IsType<CreateBootstrapTokenResult.Failure>(result);
        await _enrollmentRepo.DidNotReceive().AddTokenAsync(Arg.Any<EnrollmentToken>(), Arg.Any<CancellationToken>());
    }
}
