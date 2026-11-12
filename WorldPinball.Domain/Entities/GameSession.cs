using WorldPinball.Domain.Common;
using WorldPinball.Domain.Enums;
using WorldPinball.Domain.Events;

namespace WorldPinball.Domain.Entities;

/// <summary>
///     Root Aggregate managing the full lifecycle state machine of a connected pinball session
///     (Lobby -> Matchmaking -> Active gameplay -> Completion) and emitting domain events
///     to synchronize distributed services (Kafka, SignalR, Leaderboards).
/// </summary>
public sealed class GameSession : Entity
{
    private readonly List<GameSessionPlayer> _players = [];


    private readonly List<ScoreEntry> _scoreEntries = [];
    private readonly List<GameSlot> _slots = [];

    private GameSession()
    {
        // intitialize 4 gameslot
        for (var i = 1; i <= 4; i++) _slots.Add(new GameSlot(i));
    }


    //Constructor
    public GameSession(Guid machineId, Guid deviceId,
        Guid modeId, string clientVersion, string nonce, DateTime startedAtUtc) : this()
    {
        if (machineId == Guid.Empty)
            throw new ArgumentException(ErrorMessages.MachineId, nameof(machineId));

        if (deviceId == Guid.Empty)
            throw new ArgumentException(ErrorMessages.DeviceId, nameof(deviceId));

        if (modeId == Guid.Empty)
            throw new ArgumentException(ErrorMessages.ModeId, nameof(modeId));

        if (string.IsNullOrWhiteSpace(clientVersion))
            throw new ArgumentException(ErrorMessages.ClientVersion, nameof(clientVersion));

        if (string.IsNullOrWhiteSpace(nonce))
            throw new ArgumentException(ErrorMessages.Nonce, nameof(nonce));

        if (startedAtUtc == default)
            throw new ArgumentException(ErrorMessages.StartedAtUtc, nameof(startedAtUtc));


        SessionId = Guid.NewGuid();
        MachineId = machineId;
        DeviceId = deviceId;
        ModeId = modeId;
        ClientVersion = clientVersion;
        Nonce = nonce;
        // StartedAtUtc is initially set to the creation time, can be updated on Launch
        StartedAtUtc = startedAtUtc;
        State = GameSessionState.Created;

        AddDomainEvent(new GameSessionCreatedDomainEvent(
            SessionId,
            MachineId,
            DeviceId,
            ModeId,
            startedAtUtc));
    }

    public IReadOnlyCollection<GameSlot> Slots
    {
        get
        {
            foreach (var slot in _slots)
            {
                var player = _players.FirstOrDefault(p => p.SlotNumber == slot.SlotNumber);
                if (player is not null && slot.Status != GameSlotStatus.Occupied)
                {
                    slot.Occupy(player.UserId, player.GuestInitials);
                }
                else if (player is null && slot.Status == GameSlotStatus.Occupied)
                {
                    slot.Free();
                }

                if (player is null && State is GameSessionState.Started or GameSessionState.InGame &&
                    slot.Status == GameSlotStatus.Open)
                {
                    slot.Lock();
                }
            }

            return _slots.AsReadOnly();
        }
    }

    public IReadOnlyCollection<ScoreEntry> ScoreEntries => _scoreEntries.AsReadOnly();

    public Guid SessionId { get; }

    public Guid MachineId { get; }
    public Machine Machine { get; private set; } = null!;

    public Guid DeviceId { get; }
    public Device Device { get; private set; } = null!;

    public Guid ModeId { get; }
    public GameMode Mode { get; private set; } = null!;

    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }

    public GameSessionState State { get; private set; }

    /// <summary>
    ///     Unique cryptographic salt generated at session creation.
    ///     Used as anti-replay protection to bind machine score signatures strictly to this session.
    /// </summary>
    public string Nonce { get; private set; } = null!;

    public string ClientVersion { get; private set; } = null!;


    /// <summary>
    ///     Determines score telemetry ingestion mode:
    ///     - <see cref="TelemetryMode.BallCheckpointOnly" />: Persisted per ball drained; updates Redis leaderboard.
    ///     - <see cref="TelemetryMode.LiveStreaming" />: High-frequency telemetry broadcasted in real time via SignalR.
    /// </summary>
    public TelemetryMode TelemetryMode { get; private set; } = TelemetryMode.BallCheckpointOnly;

    public IReadOnlyCollection<GameSessionPlayer> Players => _players.AsReadOnly();

    private static class ErrorMessages
    {
        public const string MachineId = "MachineId cannot be empty.";
        public const string DeviceId = "DeviceId cannot be empty";
        public const string ModeId = "ModeId cannot be empty.";
        public const string Nonce = "Nonce cannot be empty.";
        public const string StartedAtUtc = "StartedAtUtc must be a valid date.";
        public const string ClientVersion = "ClientVersion cannot be empty.";
        public const string EndSession = "Cannot end a session that is not in Started state.";
        public const string Earlier = "EndedAtUtc cannot be earlier than StartedAtUtc.";
        public const string PauseSession = "Can only pause an active session.";
        public const string ResumeSession = "Can only resume a paused session.";
        public const string ClosedSession = "Session is already closed.";
        public const string AddPlayerNotStarted = "Cannot add a player to a session that is not in a joinable state.";
        public const string InvalidSlotNumber = "Slot number must be between 1 and 4.";
        public const string MaxPlayersReached = "A game session cannot have more than 4 players.";
        public const string SlotAlreadyOccupied = "The specified slot is already occupied.";
        public const string UserAlreadyRegistered = "This user is already registered in this session.";

        public const string RegisteredUserRequired =
            "A registered player (UserId) is required for this game mode or tournament.";

        public const string CannotSearchPlayers =
            "Can only search for players from a created, pending, or match-found session.";

        public const string MatchFoundNotSearching =
            "Cannot mark match as found unless currently searching for players.";

        public const string NotEnoughPlayersForMatch = "Cannot mark match as found without enough players.";

        public const string CannotRemovePlayerActiveSession =
            "Cannot remove a player once the session has started or ended.";

        public const string PlayerNotFound = "Player not found in the specified slot.";
        public const string CannotStartSession = "Can only start a session that is in Created or MatchFound state.";
        public const string CannotCancelSession = "Cannot cancel a session that is already active or closed.";
        public const string CannotLaunchSession = "Cannot launch a session that is not in a valid joinable state.";
        public const string AtLeastOnePlayerRequired = "Cannot launch a session without at least one player.";

        public const string CannotRecordBallInactiveSession =
            "Cannot record ball score on a session that is not active.";

        public const string InvalidBallNumber = "Ball number must be between 1 and 5.";
        public const string InvalidScore = "Score cannot be negative.";
    }


    public sealed class GameSlot
    {
        internal GameSlot(int slotNumber)
        {
            SlotNumber = slotNumber;
        }

        public int SlotNumber { get; }

        public GameSlotStatus Status { get; private set; } = GameSlotStatus.Open;
        public Guid? UserId { get; private set; }
        public string? GuestInitials { get; private set; }


        internal void Occupy(Guid? userId, string? initials)
        {
            if (Status is GameSlotStatus.Locked)
                throw new InvalidOperationException("Locked slot.");
            Status = GameSlotStatus.Occupied;
            UserId = userId;
            GuestInitials = initials;
        }

        internal void Lock()
        {
            if (Status is GameSlotStatus.Open)
                Status = GameSlotStatus.Locked;
        }

        internal void Free()
        {
            if (Status is not GameSlotStatus.Locked)
            {
                Status = GameSlotStatus.Open;
                UserId = null;
                GuestInitials = null;
            }
        }
    }

// -------------------- Lobby & Matchmaking --------------------

    #region Lobby & Matchmaking

    public void AddPlayer(int slotNumber, Guid? userId = null, string?
        guestInitials = null, bool requiresRegisteredUser = true)
    {
        if (State is not (
            GameSessionState.Created
            or GameSessionState.Pending
            or GameSessionState.SearchingPlayer
            or GameSessionState.Joined
            ))
            throw new InvalidOperationException(ErrorMessages.AddPlayerNotStarted);

        if (slotNumber is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(slotNumber), ErrorMessages.InvalidSlotNumber);

        if (requiresRegisteredUser && !userId.HasValue)
            throw new InvalidOperationException(ErrorMessages.RegisteredUserRequired);


        if (_players.Count >= 4)
            throw new InvalidOperationException(ErrorMessages.MaxPlayersReached);


        if (_players.Any(p => p.SlotNumber == slotNumber))
            throw new InvalidOperationException(ErrorMessages.SlotAlreadyOccupied);

        if (userId.HasValue && _players.Any(p => p.UserId == userId.Value))
            throw new InvalidOperationException(ErrorMessages.UserAlreadyRegistered);

        var targetSlot = _slots.First(s => s.SlotNumber == slotNumber);
        targetSlot.Occupy(userId, guestInitials);

        _players.Add(new GameSessionPlayer
        {
            SessionId = SessionId,
            SlotNumber = slotNumber,
            UserId = userId,
            GuestInitials = guestInitials,
            JoinedAtUtc = DateTime.UtcNow
        });

        if (State is GameSessionState.Created or GameSessionState.Pending) State = GameSessionState.Joined;
    }

    public void RemovePlayer(int slotNumber)
    {
        if (State is not (GameSessionState.Created
            or GameSessionState.Pending
            or GameSessionState.SearchingPlayer
            or GameSessionState.MatchFound
            or GameSessionState.Joined))
            throw new InvalidOperationException(ErrorMessages.CannotRemovePlayerActiveSession);

        var player = _players.FirstOrDefault(p => p.SlotNumber == slotNumber);
        if (player is null)
            throw new InvalidOperationException(ErrorMessages.PlayerNotFound);
        var targetSlot = _slots.First(s => s.SlotNumber == slotNumber);
        targetSlot.Free();
        _players.Remove(player);
        if (State is GameSessionState.MatchFound) State = GameSessionState.Pending;

        if (_players.Count == 0 && State is GameSessionState.Joined) State = GameSessionState.Created;
    }

    public void SearchPlayers()
    {
        if (State is not (GameSessionState.Created or GameSessionState.Pending or GameSessionState.Joined
            or GameSessionState.MatchFound))
            throw new InvalidOperationException(ErrorMessages.CannotSearchPlayers);

        State = GameSessionState.SearchingPlayer;
    }

    public void SetMatchFound(DateTime matchFoundAtUtc)
    {
        if (State is not GameSessionState.SearchingPlayer)
            throw new InvalidOperationException(ErrorMessages.MatchFoundNotSearching);
        if (_players.Count < 2)
            throw new InvalidOperationException(ErrorMessages.NotEnoughPlayersForMatch);

        State = GameSessionState.MatchFound;
        AddDomainEvent(new GameSessionMatchFoundDomainEvent(
            SessionId,
            MachineId,
            _players.Count,
            matchFoundAtUtc));
    }

    #endregion


// -------------------- Session Lifecycle --------------------

    #region Session Lifecycle

    public void Launch(DateTime launchedAtUtc)
    {
        if (State is not (GameSessionState.Created
            or GameSessionState.Pending
            or GameSessionState.SearchingPlayer
            or GameSessionState.Joined))
            throw new InvalidOperationException(ErrorMessages.CannotLaunchSession);

        if (_players.Count == 0)
            throw new InvalidOperationException(ErrorMessages.AtLeastOnePlayerRequired);
        foreach (var slot in _slots.Where(s => s.Status == GameSlotStatus.Open)) slot.Lock();

        StartedAtUtc = launchedAtUtc;
        State = GameSessionState.Started;

        AddDomainEvent(new GameSessionLaunchedDomainEvent(
            SessionId,
            MachineId,
            launchedAtUtc
        ));
    }

    public void Pause()
    {
        if (State is not (GameSessionState.Started or GameSessionState.InGame or GameSessionState.Resumed))
            throw new InvalidOperationException(ErrorMessages.PauseSession);

        State = GameSessionState.Paused;
    }

    public void Resume()
    {
        if (State is not GameSessionState.Paused)
            throw new InvalidOperationException(ErrorMessages.ResumeSession);

        State = GameSessionState.Resumed;
    }

    public void End(DateTime endedAtUtc)
    {
        if (State is not (GameSessionState.Started or GameSessionState.InGame or GameSessionState.Resumed
            or GameSessionState.Paused))
            throw new InvalidOperationException(ErrorMessages.EndSession);
        if (endedAtUtc < StartedAtUtc)
            throw new ArgumentException(ErrorMessages.Earlier, nameof(endedAtUtc));
        EndedAtUtc = endedAtUtc;
        State = GameSessionState.Ended;

        AddDomainEvent(new GameSessionEndedDomainEvent(SessionId, endedAtUtc));
    }

    public void Abandon(DateTime abandonedAtUtc)
    {
        if (State is GameSessionState.Ended or GameSessionState.Abandoned or GameSessionState.Aborted
            or GameSessionState.Cancelled)
            throw new InvalidOperationException(ErrorMessages.ClosedSession);
        if (abandonedAtUtc < StartedAtUtc)
            throw new ArgumentException(ErrorMessages.Earlier, nameof(abandonedAtUtc));
        EndedAtUtc = abandonedAtUtc;
        State = GameSessionState.Abandoned;
        AddDomainEvent(new GameSessionAbortedDomainEvent(SessionId, MachineId, abandonedAtUtc));
    }

    public void Abort(DateTime abortedAtUtc)
    {
        if (State is GameSessionState.Ended or GameSessionState.Abandoned or GameSessionState.Aborted
            or GameSessionState.Cancelled)
            throw new InvalidOperationException(ErrorMessages.ClosedSession);
        if (abortedAtUtc < StartedAtUtc)
            throw new ArgumentException(ErrorMessages.Earlier, nameof(abortedAtUtc));
        EndedAtUtc = abortedAtUtc;
        State = GameSessionState.Aborted;
        AddDomainEvent(new GameSessionAbortedDomainEvent(SessionId, MachineId, abortedAtUtc));
    }

    #endregion


// -------------------- Gameplay & Telemetry --------------------

    #region Gameplay & Telemetry

    public void RecordBallScore(int slotNumber, int ballNumber, long score, DateTime recordedAtUtc)
    {
        if (State is not (GameSessionState.Started or GameSessionState.InGame or GameSessionState.Resumed))
            throw new InvalidOperationException(ErrorMessages.CannotRecordBallInactiveSession);

        // Verify player is registered in the specified slot
        var player = _players.FirstOrDefault(p => p.SlotNumber == slotNumber);
        if (player is null)
            throw new InvalidOperationException(ErrorMessages.PlayerNotFound);

        if (ballNumber is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(ballNumber), ErrorMessages.InvalidBallNumber);

        if (score < 0)
            throw new ArgumentOutOfRangeException(nameof(score), ErrorMessages.InvalidScore);


        AddDomainEvent(new BallScoreRecordedDomainEvent(
            SessionId,
            MachineId,
            slotNumber,
            ballNumber,
            score,
            recordedAtUtc
        ));
    }

    public void EnableLiveStreaming()
    {
        if (State is GameSessionState.Ended or GameSessionState.Abandoned or GameSessionState.Aborted
            or GameSessionState.Cancelled)
            throw new InvalidOperationException(ErrorMessages.ClosedSession);
        // Switch telemetry mode to high-frequency streaming
        TelemetryMode = TelemetryMode.LiveStreaming;
    }

    #endregion
}