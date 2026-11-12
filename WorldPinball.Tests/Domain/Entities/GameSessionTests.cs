using FluentAssertions;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;
using WorldPinball.Domain.Events;

namespace WorldPinball.Tests.Domain.Entities;

public class GameSessionTests
{
    private static GameSession CreateSession(DateTime? startedAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0.0", "nonce", startedAt ?? DateTime.UtcNow);

    private static GameSession CreateSessionWithPlayer(DateTime? startedAt = null)
    {
        var session = CreateSession(startedAt);
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        return session;
    }

    
// -------------------- Constructor --------------------
    #region Constructor
    [Fact]
    public void Constructor_WhenArgumentsAreValid_ShouldInitializeSessionInCreatedState()
    {
        // Arrange
        var machineId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var modeId = Guid.NewGuid();
        var clientVersion = "1.0.0";
        var nonce = "nonce-random-xyz";
        var startedAtUtc = DateTime.UtcNow;

        // Act
        var session = new GameSession(
            machineId,
            deviceId,
            modeId,
            clientVersion,
            nonce,
            startedAtUtc
        );

        // Assert
        session.SessionId.Should().NotBeEmpty();
        session.MachineId.Should().Be(machineId);
        session.DeviceId.Should().Be(deviceId);
        session.ModeId.Should().Be(modeId);
        session.ClientVersion.Should().Be(clientVersion);
        session.Nonce.Should().Be(nonce);
        session.StartedAtUtc.Should().Be(startedAtUtc);
        session.EndedAtUtc.Should().BeNull();
        session.State.Should().Be(GameSessionState.Created);
    }

    [Fact]
    public void Constructor_WhenMachineIdIsEmpty_ThrowsArgumentException()
    {
        // Act
        Action act = () => new GameSession(
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1.0.0",
            "nonce-xyz",
            DateTime.UtcNow
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenDeviceIdIsEmpty_ThrowsArgumentException()
    {
        // Act
        Action act = () => new GameSession(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            "1.0.0",
            "nonce-xyz",
            DateTime.UtcNow
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenModeIdIsEmpty_ThrowsArgumentException()
    {
        // Act
        Action act = () => new GameSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            "1.0.0",
            "nonce-xyz",
            DateTime.UtcNow
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenClientVersionIsInvalid_ThrowsArgumentException(string? invalidClientVersion)
    {
        // Act
        Action act = () => new GameSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            invalidClientVersion!,
            "nonce-xyz",
            DateTime.UtcNow
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenNonceIsInvalid_ThrowsArgumentException(string? invalidNonce)
    {
        // Act
        Action act = () => new GameSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1.0.0",
            invalidNonce!,
            DateTime.UtcNow
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenStartedAtUtcIsDefault_ThrowsArgumentException()
    {
        // Act
        Action act = () => new GameSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1.0.0",
            "nonce-xyz",
            default
        );

        // Assert
        act.Should().Throw<ArgumentException>();
    }
    #endregion

// -------------------- Lobby & Matchmaking --------------------
    
    #region Lobby & Matchmaking
    [Fact]
    public void AddPlayer_WhenPlayerIsValid_ShouldAddToPlayersCollection()
    {
        // Arrange
        var session = CreateSession();
        var userId = Guid.NewGuid();

        // Act
        session.AddPlayer(slotNumber: 1, userId: userId);

        // Assert
        session.Players.Should().HaveCount(1);
        var player = session.Players.First();
        player.SlotNumber.Should().Be(1);
        player.UserId.Should().Be(userId);
        player.GuestInitials.Should().BeNull();
        player.SessionId.Should().Be(session.SessionId);
    }

    [Fact]
    public void AddPlayer_WhenAnonymousGuest_ShouldAddToPlayersCollection()
    {
        // Arrange
        var session = CreateSession();

        // Act
        session.AddPlayer(slotNumber: 2, userId: null, guestInitials: "AAA", requiresRegisteredUser: false);

        // Assert
        session.Players.Should().HaveCount(1);
        var player = session.Players.First();
        player.SlotNumber.Should().Be(2);
        player.UserId.Should().BeNull();
        player.GuestInitials.Should().Be("AAA");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void AddPlayer_WhenSlotNumberIsOutOfRange_ShouldThrowArgumentOutOfRangeException(int invalidSlotNumber)
    {
        // Arrange
        var session = CreateSession();

        // Act
        Action act = () => session.AddPlayer(slotNumber: invalidSlotNumber);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddPlayer_WhenRegistredUserRequieredAndUserIdIsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();

        // Act
        Action act = () => session.AddPlayer(slotNumber: 1, userId: null, requiresRegisteredUser: true);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPlayer_WhenSlotIsAlreadyOccupied_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Act: Attempt to add another player on slot 1
        Action act = () => session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPlayer_WhenSessionHas4Players_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        session.AddPlayer(slotNumber: 2, userId: Guid.NewGuid());
        session.AddPlayer(slotNumber: 3, userId: Guid.NewGuid());
        session.AddPlayer(slotNumber: 4, userId: Guid.NewGuid());

        // Act: Attempt to add a 5th player
        Action act = () => session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPlayer_WhenSameUserRegisteredTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        var userId = Guid.NewGuid();
        session.AddPlayer(slotNumber: 1, userId: userId);

        // Act: Same user tries to occupy slot 2
        Action act = () => session.AddPlayer(slotNumber: 2, userId: userId);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPlayer_WhenSessionIsEnded_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);
        session.End(DateTime.UtcNow.AddMinutes(5));

        // Act
        Action act = () => session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPlayer_WhenSessionIsCreated_TransitionsStateToJoined()
    {
        // Arrange
        var session = CreateSession();
        session.State.Should().Be(GameSessionState.Created);

        // Act
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Assert
        session.State.Should().Be(GameSessionState.Joined);
    }

    [Fact]
    public void RemovePlayer_WhenLastPlayerRemoved_TransitionsStateBackToCreated()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        session.State.Should().Be(GameSessionState.Joined);

        // Act
        session.RemovePlayer(slotNumber: 1);

        // Assert
        session.State.Should().Be(GameSessionState.Created);
    }
    #endregion
    
// -------------------- Session Lifecycle --------------------

    #region Session Lifecycle
    [Fact]
    public void Launch_WhenSessionIsJoined_TransitionsStateToStarted()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        session.State.Should().Be(GameSessionState.Joined);

        // Act
        session.Launch(DateTime.UtcNow);

        // Assert
        session.State.Should().Be(GameSessionState.Started);
    }

    [Fact]
    public void Launch_WhenNoPlayersInSession_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();

        // Act
        Action act = () => session.Launch(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot launch a session without at least one player.");
    }

    [Fact]
    public void End_WhenSessionIsStarted_ShouldSetEndedAtAndTransitionToEndedState()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        var endedAt = startedAt.AddMinutes(10);

        // Act
        session.End(endedAt);

        // Assert
        session.EndedAtUtc.Should().Be(endedAt);
        session.State.Should().Be(GameSessionState.Ended);
    }

    [Fact]
    public void End_WhenSessionIsResumed_ShouldTransitionToEndedState()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        session.Pause();
        session.Resume();
        var endedAt = startedAt.AddMinutes(15);

        // Act
        session.End(endedAt);

        // Assert
        session.EndedAtUtc.Should().Be(endedAt);
        session.State.Should().Be(GameSessionState.Ended);
    }

    [Fact]
    public void End_WhenSessionIsPaused_ShouldTransitionToEndedState()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        session.Pause();
        var endedAt = startedAt.AddMinutes(10);

        // Act
        session.End(endedAt);

        // Assert
        session.EndedAtUtc.Should().Be(endedAt);
        session.State.Should().Be(GameSessionState.Ended);
    }

    [Fact]
    public void End_WhenEndedAtEqualsStartedAt_ShouldSucceed()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);

        // Act
        session.End(startedAt);

        // Assert
        session.EndedAtUtc.Should().Be(startedAt);
        session.State.Should().Be(GameSessionState.Ended);
    }

    [Fact]
    public void End_WhenSessionIsNotInActiveState_ThrowsInvalidOperationException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        session.End(startedAt.AddMinutes(5));

        // Act: Attempt to close an already ended session
        Action act = () => session.End(startedAt.AddMinutes(10));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void End_WhenEndedAtIsEarlierThanStartedAt_ThrowsArgumentException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);

        // Act: End date earlier than start date
        Action act = () => session.End(startedAt.AddMinutes(-5));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pause_WhenSessionIsStarted_ShouldTransitionToPausedState()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);

        // Act
        session.Pause();

        // Assert
        session.State.Should().Be(GameSessionState.Paused);
    }

    [Fact]
    public void Pause_WhenSessionIsResumed_ShouldTransitionToPausedState()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);
        session.Pause();
        session.Resume();

        // Act
        session.Pause();

        // Assert
        session.State.Should().Be(GameSessionState.Paused);
    }

    [Fact]
    public void Pause_WhenSessionIsNotActive_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);
        session.Pause(); // Already paused

        // Act: Attempt to pause again
        Action act = () => session.Pause();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resume_WhenSessionIsPaused_ShouldTransitionToResumedState()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);
        session.Pause();

        // Act
        session.Resume();

        // Assert
        session.State.Should().Be(GameSessionState.Resumed);
    }

    [Fact]
    public void Resume_WhenSessionIsNotPaused_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow); // Started state

        // Act: Attempt to resume without pausing first
        Action act = () => session.Resume();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Abandon_WhenSessionIsActive_ShouldTransitionToAbandonedState()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        var abandonedAt = startedAt.AddMinutes(3);

        // Act
        session.Abandon(abandonedAt);

        // Assert
        session.EndedAtUtc.Should().Be(abandonedAt);
        session.State.Should().Be(GameSessionState.Abandoned);
    }

    [Fact]
    public void Abandon_WhenSessionIsAlreadyClosed_ThrowsInvalidOperationException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        session.Abandon(startedAt.AddMinutes(5));

        // Act: Attempt to abandon an already closed session
        Action act = () => session.Abandon(startedAt.AddMinutes(10));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Abandon_WhenAbandonedAtIsEarlierThanStartedAt_ThrowsArgumentException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);

        // Act: Abandon date earlier than start date
        Action act = () => session.Abandon(startedAt.AddMinutes(-5));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Abort_WhenSessionIsActive_ShouldTransitionToAbortedState()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        var abortedAt = startedAt.AddMinutes(2);

        // Act
        session.Abort(abortedAt);

        // Assert
        session.EndedAtUtc.Should().Be(abortedAt);
        session.State.Should().Be(GameSessionState.Aborted);
    }

    [Fact]
    public void Abort_WhenSessionIsAlreadyClosed_ThrowsInvalidOperationException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);
        session.Abort(startedAt.AddMinutes(5));

        // Act: Attempt to abort an already closed session
        Action act = () => session.Abort(startedAt.AddMinutes(10));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Abort_WhenAbortedAtIsEarlierThanStartedAt_ThrowsArgumentException()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var session = CreateSessionWithPlayer(startedAt);
        session.Launch(startedAt);

        // Act: Abort date earlier than start date
        Action act = () => session.Abort(startedAt.AddMinutes(-5));

        // Assert
        act.Should().Throw<ArgumentException>();
    }
    #endregion
    
// --------------------  Gameplay & Telemetry --------------------
    
    #region Gameplay & Telemetry
    [Fact]
    public void RecordBallScore_WhenSessionNotStarted_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession(); // State = Created
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());

        // Act
        Action act = () => session.RecordBallScore(slotNumber: 1, ballNumber: 1, score: 50_000, DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot record ball score on a session that is not active.");
    }

    [Fact]
    public void RecordBallScore_WhenPlayerNotInSlot_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSessionWithPlayer();
        session.Launch(DateTime.UtcNow);

        // Act
        Action act = () => session.RecordBallScore(slotNumber: 2, ballNumber: 1, score: 10_000, DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player not found in the specified slot.");
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void RecordBallScore_WhenBallNumberIsOutOfRange_ShouldThrowArgumentOutOfRangeException(int invalidBallNumber)
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        session.Launch(DateTime.UtcNow);

        // Act
        Action act = () => session.RecordBallScore(slotNumber: 1, ballNumber: invalidBallNumber, score: 50_000, DateTime.UtcNow);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    
    
    [Fact]
    public void RecordBallScore_WhenScoreIsNegative_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        session.Launch(DateTime.UtcNow);

        // Act
        Action act = () => session.RecordBallScore(slotNumber: 1, ballNumber: 1, score: -100, DateTime.UtcNow);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    
    [Fact]
    public void RecordBallScore_WhenArgumentsAreValid_ShouldEmitBallScoreRecordedDomainEvent()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, userId: Guid.NewGuid());
        var recordedAt = DateTime.UtcNow;
        session.Launch(recordedAt);

        // Act
        session.RecordBallScore(slotNumber: 1, ballNumber: 2, score: 750_000, recordedAt);

        // Assert
        var domainEvent = session.DomainEvents.OfType<BallScoreRecordedDomainEvent>().SingleOrDefault();
        domainEvent.Should().NotBeNull();
        domainEvent!.SessionId.Should().Be(session.SessionId);
        domainEvent.SlotNumber.Should().Be(1);
        domainEvent.BallNumber.Should().Be(2);
        domainEvent.Score.Should().Be(750_000);
    }
    
    [Fact]
    public void Constructor_ByDefault_ShouldSetTelemetryModeToBallCheckpointOnly()
    {
        // Arrange & Act
        var session = CreateSession();

        // Assert
        session.TelemetryMode.Should().Be(TelemetryMode.BallCheckpointOnly);
    }
    
    [Fact]
    public void EnableLiveStreaming_WhenSessionIsActive_ShouldSetTelemetryModeToLiveStreaming()
    {
        // Arrange
        var session = CreateSession();
        // Act
        session.EnableLiveStreaming();
        // Assert
        session.TelemetryMode.Should().Be(TelemetryMode.LiveStreaming);
    }
    
    #endregion
    

    // -------------------- Slots Lifecycle Tests --------------------
    #region Slots Lifecycle

    [Fact]
    public void Constructor_ShouldInitializeFourOpenSlots()
    {
        // Act
        var session = CreateSession();

        // Assert
        session.Slots.Should().HaveCount(4);
        session.Slots.Select(s => s.SlotNumber).Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
        session.Slots.Should().OnlyContain(s => s.Status == GameSlotStatus.Open && s.UserId == null && s.GuestInitials == null);
    }

    [Fact]
    public void AddPlayer_ShouldMarkSlotAsOccupied()
    {
        // Arrange
        var session = CreateSession();
        var userId = Guid.NewGuid();

        // Act
        session.AddPlayer(slotNumber: 2, userId: userId);

        // Assert
        var slot2 = session.Slots.First(s => s.SlotNumber == 2);
        slot2.Status.Should().Be(GameSlotStatus.Occupied);
        slot2.UserId.Should().Be(userId);

        session.Slots.Where(s => s.SlotNumber != 2)
            .Should().OnlyContain(s => s.Status == GameSlotStatus.Open);
    }

    [Fact]
    public void RemovePlayer_ShouldResetSlotToOpen()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, guestInitials: "TOM", requiresRegisteredUser: false);
        session.Slots.First(s => s.SlotNumber == 1).Status.Should().Be(GameSlotStatus.Occupied);

        // Act
        session.RemovePlayer(slotNumber: 1);

        // Assert
        var slot1 = session.Slots.First(s => s.SlotNumber == 1);
        slot1.Status.Should().Be(GameSlotStatus.Open);
        slot1.GuestInitials.Should().BeNull();
        slot1.UserId.Should().BeNull();
    }

    [Fact]
    public void Launch_ShouldLockAllRemainingOpenSlots()
    {
        // Arrange
        var session = CreateSession();
        session.AddPlayer(slotNumber: 1, guestInitials: "AAA", requiresRegisteredUser: false);
        session.AddPlayer(slotNumber: 3, guestInitials: "BBB", requiresRegisteredUser: false);

        // Act
        session.Launch(DateTime.UtcNow);

        // Assert
        session.Slots.First(s => s.SlotNumber == 1).Status.Should().Be(GameSlotStatus.Occupied);
        session.Slots.First(s => s.SlotNumber == 3).Status.Should().Be(GameSlotStatus.Occupied);
        session.Slots.First(s => s.SlotNumber == 2).Status.Should().Be(GameSlotStatus.Locked);
        session.Slots.First(s => s.SlotNumber == 4).Status.Should().Be(GameSlotStatus.Locked);
    }

    #endregion

}