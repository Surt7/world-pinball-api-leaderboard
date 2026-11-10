using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using WorldPinball.Api.Contracts.Sessions;
using WorldPinball.Api.Controllers.Public;
using WorldPinball.Api.Filters.Security;
using WorldPinball.Application.Features.GameSessions.Public.AbortSession;
using WorldPinball.Application.Features.GameSessions.Public.AbortSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer;
using WorldPinball.Application.Features.GameSessions.Public.AddPlayer.Commands;
using WorldPinball.Application.Features.GameSessions.Public.CreateGameSession;
using WorldPinball.Application.Features.GameSessions.Public.CreateGameSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Dtos;
using WorldPinball.Application.Features.GameSessions.Public.GetLobby.Queries;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer;
using WorldPinball.Application.Features.GameSessions.Public.KickPlayer.Commands;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession;
using WorldPinball.Application.Features.GameSessions.Public.LaunchSession.Commands;
using WorldPinball.Application.Features.GameSessions.Public.SubmitScore;
using WorldPinball.Application.Features.GameSessions.Public.SubmitScore.Commands;
using WorldPinball.Application.Interfaces.Repositories;
using WorldPinball.Application.Interfaces.Services;
using WorldPinball.Application.Security.Cryptography;
using WorldPinball.Domain.Entities;

namespace WorldPinball.Tests.Controller;

public class SessionsControllerTests
{

    private readonly ICreateGameSessionHandler _createSessionHandler = Substitute.For<ICreateGameSessionHandler>();
    private readonly ISubmitScoreHandler _submitScoreHandler = Substitute.For<ISubmitScoreHandler>();
    private readonly IAbortSessionHandler _abortSessionHandler = Substitute.For<IAbortSessionHandler>();
    private readonly IGetLobbyHandler _getLobbyHandler = Substitute.For<IGetLobbyHandler>();
    private readonly IAddPlayerHandler _addPlayerHandler = Substitute.For<IAddPlayerHandler>();
    private readonly IKickPlayerHandler _kickPlayerHandler = Substitute.For<IKickPlayerHandler>();
    private readonly ILaunchSessionHandler _launchSessionHandler = Substitute.For<ILaunchSessionHandler>();

    private readonly IValidator<CreateGameSessionRequest> _validatorCreateSession =
        Substitute.For<IValidator<CreateGameSessionRequest>>();

    private readonly IValidator<SubmitScoreRequest> _validatorSubmitScore =
        Substitute.For<IValidator<SubmitScoreRequest>>();

    private readonly IValidator<AddPlayerRequest> _validatorAddPlayer = Substitute.For<IValidator<AddPlayerRequest>>();
    private readonly ILobbyNotificationService _lobbyNotifier = Substitute.For<ILobbyNotificationService>();
    private readonly IGameSessionWriteRepository _sessionRepository = Substitute.For<IGameSessionWriteRepository>();
    private readonly IAuthorizationService _authorization = Substitute.For<IAuthorizationService>();


    private SessionsController CreateController()
    {
        var dummySession =
            new GameSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", "nonce", DateTime.UtcNow);
        _sessionRepository.GetSessionWithMachineAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(dummySession);

        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Success());

        _validatorCreateSession.ValidateAsync(Arg.Any<CreateGameSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _validatorSubmitScore.ValidateAsync(Arg.Any<SubmitScoreRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _validatorAddPlayer.ValidateAsync(Arg.Any<AddPlayerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var controller = new SessionsController(
            _createSessionHandler,
            _submitScoreHandler,
            _abortSessionHandler,
            _getLobbyHandler,
            _addPlayerHandler,
            _kickPlayerHandler,
            _launchSessionHandler,
            _lobbyNotifier,
            _sessionRepository,
            _authorization
        );

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task CreateGameSession_WhenRequestIsValidAuthenticated_ShouldReturnOk()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sessionId = Guid.NewGuid();
        string fakeSignature = RandomNumberGenerator.GetHexString(16);
        var now = DateTimeOffset.UtcNow;



        _createSessionHandler.HandleAsync(Arg.Any<CreateGameSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CreateGameSessionResult.Success(sessionId, nonce, now));

        var controller = CreateController();
        controller.HttpContext.Request.Method = "POST";
        controller.HttpContext.Request.Path = "/api/v1/sessions/create";
        controller.HttpContext.Items[MachineSignatureFilter.DeviceIdItemKey] = deviceId;
        var request = new CreateGameSessionRequest("STORY", "1.02.12");

        // Act
        var result = await controller.CreateGameSession(request, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeAssignableTo<CreateGameSessionResponse>().Which;
        response.SessionId.Should().Be(sessionId);
        response.ServerTime.Should().Be(now.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task SubmitScore_WhenRequestIsValid_ShouldReturnOk()
    {
        // Arrange
        var deviceId = Guid.NewGuid();
        var scoreGuid = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _submitScoreHandler.HandleAsync(Arg.Any<SubmitScoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SubmitScoreResult.Success([scoreGuid]));

        var controller = CreateController();
        var request = new SubmitScoreRequest(deviceId, [new SlotScoreRequest(1, 5000)]);

        // Act
        var result = await controller.SubmitScore(sessionId, request, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<SubmitScoreResponse>().Which;
        response.ScoreEntryIds.Should().ContainSingle().Which.Should().Be(scoreGuid);
    }

    [Fact]
    public async Task GetLobby_WhenSessionExists_ShouldReturnOk()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var lobbyDto = new SessionLobbyDto(sessionId, "Created", []);
        _getLobbyHandler.HandleAsync(Arg.Is<GetLobbyQuery>(q => q.SessionId == sessionId), Arg.Any<CancellationToken>())
            .Returns(new GetLobbyResult.Success(lobbyDto));

        var controller = CreateController();

        // Act
        var result = await controller.GetLobby(sessionId, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<SessionLobbyDto>().Which;
        response.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task AddPlayer_WhenRequestIsValid_ShouldReturnOk()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _addPlayerHandler.HandleAsync(Arg.Is<AddPlayerCommand>(c => c.SessionId == sessionId && c.SlotNumber == 1),
                Arg.Any<CancellationToken>())
            .Returns(new AddPlayerResult.Success(sessionId, 1));

        var controller = CreateController();
        var request = new AddPlayerRequest(1, null, "AAA");

        // Act
        var result = await controller.AddPlayer(sessionId, request, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<AddPlayerResponse>().Which;
        response.SessionId.Should().Be(sessionId);
        response.SlotNumber.Should().Be(1);
    }

    [Fact]
    public async Task KickPlayer_WhenPlayerExists_ShouldReturnOk()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _kickPlayerHandler.HandleAsync(Arg.Is<KickPlayerCommand>(c => c.SessionId == sessionId && c.SlotNumber == 2),
                Arg.Any<CancellationToken>())
            .Returns(new KickPlayerResult.Success(sessionId, 2));

        var controller = CreateController();

        // Act
        var result = await controller.KickPlayer(sessionId, 2, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<KickPlayerResponse>().Which;
        response.SlotNumber.Should().Be(2);
    }

    [Fact]
    public async Task LaunchSession_WhenSessionReady_ShouldReturnOk()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _launchSessionHandler.HandleAsync(Arg.Is<LaunchSessionCommand>(c => c.SessionId == sessionId),
                Arg.Any<CancellationToken>())
            .Returns(new LaunchSessionResult.Success(sessionId));

        var controller = CreateController();

        // Act
        var result = await controller.LaunchSession(sessionId, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<LaunchSessionResponse>().Which;
        response.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task AbortSession_WhenSessionAborted_ShouldReturnOk()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _abortSessionHandler.HandleAsync(Arg.Is<AbortSessionCommand>(c => c.SessionId == sessionId),
                Arg.Any<CancellationToken>())
            .Returns(new AbortSessionResult.Success(sessionId));

        var controller = CreateController();

        // Act
        var result = await controller.AbortSession(sessionId, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Which;
        var response = okResult.Value.Should().BeOfType<AbortSessionResponse>().Which;
        response.SessionId.Should().Be(sessionId);
    }
    
    
    [Fact]
    public async Task AbortSession_WhenUserIsNotAuthorized_ShouldReturnForbid()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var controller = CreateController();

        _authorization.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<object>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());

        // Act
        var result = await controller.AbortSession(sessionId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }
    
    [Fact]
    public async Task SubmitScore_WhenSessionNotStarted_ShouldReturnConflict()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _submitScoreHandler.HandleAsync(Arg.Any<SubmitScoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SubmitScoreResult.SessionNotStarted(sessionId));

        var controller = CreateController();
        var request = new SubmitScoreRequest(Guid.NewGuid(), [new SlotScoreRequest(1, 5000)]);

        // Act
        var result = await controller.SubmitScore(sessionId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }
    
    [Fact]
    public async Task KickPlayer_WhenUserIsNotAuthorized_ShouldReturnForbid()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var controller = CreateController();

        _authorization.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<object>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());

        // Act
        var result = await controller.KickPlayer(sessionId, 2, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }
    
    [Fact]
    public async Task LaunchSession_WhenUserIsNotAuthorized_ShouldReturnForbid()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var controller = CreateController();

        _authorization.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<object>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());

        // Act
        var result = await controller.LaunchSession(sessionId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }
    
    
    
    
}