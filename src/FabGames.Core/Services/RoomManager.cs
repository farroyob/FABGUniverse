using System.Collections.Concurrent;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace FabGames.Core.Services;

/// <inheritdoc />
public sealed class RoomManager : IRoomManager
{
    private const int MaxDisplayNameLength = 16;
    private const int MinDisplayNameLength = 2;

    private readonly IGameEngineRegistry _engines;
    private readonly TimeProvider _time;
    private readonly RoomManagerOptions _options;

    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _roomByConnection = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _connectionByPlayer = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _roomByPlayer = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _playerByConnection = new(StringComparer.Ordinal);

    public RoomManager(IGameEngineRegistry engines, TimeProvider time, IOptions<RoomManagerOptions> options)
    {
        _engines = engines;
        _time = time;
        _options = options.Value;
    }

    public int RoomCount => _rooms.Count;

    public string? GetRoomCodeOfPlayer(string playerId) =>
        playerId is not null && _roomByPlayer.TryGetValue(playerId, out var code) ? code : null;

    public string? GetPlayerIdOfConnection(string connectionId) =>
        connectionId is not null && _playerByConnection.TryGetValue(connectionId, out var id) ? id : null;

    public string? GetConnectionId(string playerId) =>
        playerId is not null && _connectionByPlayer.TryGetValue(playerId, out var id) ? id : null;

    // ───────────────────────── Room lifecycle ─────────────────────────

    public async Task<OperationResult<RoomUpdate>> CreateRoomAsync(
        string connectionId, string gameTypeId, string playerId, string displayName, string avatarId,
        TimeControl timeControl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || timeControl is null || !timeControl.IsValid
            || !TryBuildPlayer(playerId, displayName, avatarId, 0, out var player))
        {
            return OperationResult<RoomUpdate>.Fail(ErrorCodes.InvalidRequest);
        }

        if (!_engines.TryGet(gameTypeId, out var engine))
        {
            return OperationResult<RoomUpdate>.Fail(ErrorCodes.UnknownGame);
        }

        if (_rooms.Count >= _options.MaxRooms)
        {
            return OperationResult<RoomUpdate>.Fail(ErrorCodes.RoomLimitReached);
        }

        var detachError = await DetachFromOtherRoomAsync(player.PlayerId, null, ct).ConfigureAwait(false);
        if (detachError is not null)
        {
            return OperationResult<RoomUpdate>.Fail(detachError);
        }

        var now = _time.GetUtcNow();
        Room room;
        do
        {
            room = new Room
            {
                Code = RoomCodeGenerator.Generate(),
                Engine = engine,
                TimeControl = timeControl,
                CreatedAt = now,
                HostPlayerId = player.PlayerId,
                LastActivity = now,
            };
            room.Players.Add(player);
        }
        while (!_rooms.TryAdd(room.Code, room));

        RegisterConnection(room.Code, player.PlayerId, connectionId);
        return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
    }

    public async Task<OperationResult<RoomUpdate>> JoinRoomAsync(
        string connectionId, string roomCode, string playerId, string displayName, string avatarId,
        CancellationToken ct = default)
    {
        var code = RoomCodeGenerator.Normalize(roomCode);
        if (code.Length == 0)
        {
            return OperationResult<RoomUpdate>.Fail(ErrorCodes.RoomNotFound);
        }

        if (string.IsNullOrWhiteSpace(connectionId)
            || !TryBuildPlayer(playerId, displayName, avatarId, 0, out var candidate))
        {
            return OperationResult<RoomUpdate>.Fail(ErrorCodes.InvalidRequest);
        }

        var detachError = await DetachFromOtherRoomAsync(candidate.PlayerId, code, ct).ConfigureAwait(false);
        if (detachError is not null)
        {
            return OperationResult<RoomUpdate>.Fail(detachError);
        }

        return await WithRoomAsync(code, room =>
        {
            var now = _time.GetUtcNow();
            if (room.Players.Exists(p => p.PlayerId == candidate.PlayerId))
            {
                RegisterConnection(room.Code, candidate.PlayerId, connectionId);
                room.Disconnected.Remove(candidate.PlayerId);
                Touch(room, now);
                return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
            }

            if (room.Status != RoomStatus.Waiting)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.GameInProgress);
            }

            if (room.Players.Count >= room.Engine.MaxPlayers)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.RoomFull);
            }

            var seat = 0;
            while (room.Players.Exists(p => p.SeatNumber == seat))
            {
                seat++;
            }

            room.Players.Add(candidate with { SeatNumber = seat });
            RegisterConnection(room.Code, candidate.PlayerId, connectionId);
            Touch(room, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
        }, ct).ConfigureAwait(false);
    }

    public Task<OperationResult<RoomUpdate>> RejoinRoomAsync(
        string connectionId, string roomCode, string playerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || !IsValidId(playerId, 64))
        {
            return Task.FromResult(OperationResult<RoomUpdate>.Fail(ErrorCodes.InvalidRequest));
        }

        return WithRoomAsync(roomCode, room =>
        {
            if (!room.Players.Exists(p => p.PlayerId == playerId))
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotInRoom);
            }

            var now = _time.GetUtcNow();
            RegisterConnection(room.Code, playerId, connectionId);
            room.Disconnected.Remove(playerId);
            Touch(room, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
        }, ct);
    }

    public Task<OperationResult<RoomUpdate>> SetReadyAsync(
        string roomCode, string playerId, bool ready, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            if (!IsMember(room, playerId))
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotInRoom);
            }

            if (room.Status != RoomStatus.Waiting)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.GameInProgress);
            }

            var now = _time.GetUtcNow();
            if (ready)
            {
                room.Ready.Add(playerId);
            }
            else
            {
                room.Ready.Remove(playerId);
            }

            Touch(room, now);
            var everyoneReady = room.Players.Count == room.Engine.MaxPlayers
                && room.Ready.Count == room.Players.Count;
            if (everyoneReady)
            {
                StartSession(room, now);
                return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), null, true));
            }

            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
        }, ct);

    public Task<OperationResult<RoomUpdate>> StartGameAsync(
        string roomCode, string playerId, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            if (!IsMember(room, playerId))
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotInRoom);
            }

            if (room.HostPlayerId != playerId)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotHost);
            }

            if (room.Status != RoomStatus.Waiting)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.GameInProgress);
            }

            if (room.Players.Count < room.Engine.MinPlayers)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotEnoughPlayers);
            }

            var now = _time.GetUtcNow();
            StartSession(room, now);
            Touch(room, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), null, true));
        }, ct);

    // ───────────────────────── Gameplay ─────────────────────────

    public Task<OperationResult<RoomUpdate>> MakeMoveAsync(
        string roomCode, string playerId, string actionJson, int expectedStateVersion,
        CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            var error = RequireRunningGame(room, playerId, out var session);
            if (error is not null)
            {
                return OperationResult<RoomUpdate>.Fail(error);
            }

            if (string.IsNullOrWhiteSpace(actionJson) || actionJson.Length > 2048)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.InvalidAction);
            }

            if (room.Engine.GetCurrentTurnPlayerId(session.State) != playerId)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotYourTurn);
            }

            if (expectedStateVersion != session.StateVersion)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.StaleState);
            }

            var now = _time.GetUtcNow();
            var timed = room.TimeControl.IsTimed;
            var remaining = TimeSpan.Zero;
            if (timed)
            {
                remaining = session.Remaining[playerId] - (now - session.TurnStartedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    var timeout = room.Engine.DeclareTimeout(session.State, playerId);
                    EndGame(room, timeout, now);
                    Touch(room, now);
                    return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), timeout));
                }
            }

            var applied = room.Engine.ApplyAction(session.State, playerId, actionJson);
            if (!applied.Success)
            {
                return OperationResult<RoomUpdate>.Fail(applied.ErrorCode ?? ErrorCodes.IllegalMove);
            }

            session.State = applied.NewState!;
            session.StateVersion++;
            session.MoveCount++;
            session.TurnStartedAt = now;
            session.DrawOfferedBy = null;
            if (timed)
            {
                session.Remaining[playerId] = remaining + room.TimeControl.Increment;
            }

            if (applied.Result is not null)
            {
                EndGame(room, applied.Result, now);
            }

            Touch(room, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), applied.Result));
        }, ct);

    public Task<OperationResult<RoomUpdate>> ResignAsync(
        string roomCode, string playerId, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            var error = RequireRunningGame(room, playerId, out var session);
            if (error is not null)
            {
                return OperationResult<RoomUpdate>.Fail(error);
            }

            var now = _time.GetUtcNow();
            var result = room.Engine.Resign(session.State, playerId);
            EndGame(room, result, now);
            Touch(room, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), result));
        }, ct);

    public Task<OperationResult<RoomUpdate>> OfferDrawAsync(
        string roomCode, string playerId, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            var error = RequireRunningGame(room, playerId, out var session);
            if (error is not null)
            {
                return OperationResult<RoomUpdate>.Fail(error);
            }

            if (session.DrawOfferedBy == playerId)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.DrawAlreadyOffered);
            }

            var now = _time.GetUtcNow();
            Touch(room, now);
            if (session.DrawOfferedBy is not null)
            {
                var agreed = GameResult.Draw(GameEndReason.DrawAgreement);
                EndGame(room, agreed, now);
                return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), agreed));
            }

            session.DrawOfferedBy = playerId;
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
        }, ct);

    public Task<OperationResult<RoomUpdate>> RespondDrawAsync(
        string roomCode, string playerId, bool accept, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            var error = RequireRunningGame(room, playerId, out var session);
            if (error is not null)
            {
                return OperationResult<RoomUpdate>.Fail(error);
            }

            if (session.DrawOfferedBy is null || session.DrawOfferedBy == playerId)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NoDrawOffer);
            }

            var now = _time.GetUtcNow();
            Touch(room, now);
            if (!accept)
            {
                session.DrawOfferedBy = null;
                return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
            }

            var agreed = GameResult.Draw(GameEndReason.DrawAgreement);
            EndGame(room, agreed, now);
            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), agreed));
        }, ct);

    public Task<OperationResult<RoomUpdate>> RequestRematchAsync(
        string roomCode, string playerId, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            if (!IsMember(room, playerId))
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.NotInRoom);
            }

            if (room.Status != RoomStatus.Finished || room.Players.Count < room.Engine.MinPlayers)
            {
                return OperationResult<RoomUpdate>.Fail(ErrorCodes.RematchNotAvailable);
            }

            var now = _time.GetUtcNow();
            room.RematchVotes.Add(playerId);
            Touch(room, now);
            if (room.RematchVotes.Count == room.Players.Count)
            {
                StartSession(room, now);
                return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now), null, true));
            }

            return OperationResult<RoomUpdate>.Ok(new RoomUpdate(BuildSnapshot(room, now)));
        }, ct);

    public Task<OperationResult<ChatMessage>> SendChatAsync(
        string roomCode, string playerId, string message, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            var player = room.Players.Find(p => p.PlayerId == playerId);
            if (player is null)
            {
                return OperationResult<ChatMessage>.Fail(ErrorCodes.NotInRoom);
            }

            var text = new string((message ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (text.Length == 0 || text.Length > _options.MaxChatLength)
            {
                return OperationResult<ChatMessage>.Fail(ErrorCodes.MessageInvalid);
            }

            var now = _time.GetUtcNow();
            Touch(room, now);
            return OperationResult<ChatMessage>.Ok(new ChatMessage(playerId, player.DisplayName, text, now));
        }, ct);

    // ───────────────────────── Presence ─────────────────────────

    public Task<OperationResult<PlayerDeparture>> LeaveRoomAsync(
        string roomCode, string playerId, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room =>
        {
            if (!IsMember(room, playerId))
            {
                return OperationResult<PlayerDeparture>.Fail(ErrorCodes.NotInRoom);
            }

            return OperationResult<PlayerDeparture>.Ok(LeaveInternal(room, playerId, _time.GetUtcNow()));
        }, ct);

    public async Task<OperationResult<DisconnectInfo>> HandleDisconnectAsync(
        string connectionId, CancellationToken ct = default)
    {
        if (connectionId is not null)
        {
            _playerByConnection.TryRemove(connectionId, out _);
        }

        if (connectionId is null || !_roomByConnection.TryRemove(connectionId, out var code))
        {
            return OperationResult<DisconnectInfo>.Fail(ErrorCodes.NotInRoom);
        }

        return await WithRoomAsync(code, room =>
        {
            var playerId = room.Players
                .Select(p => p.PlayerId)
                .FirstOrDefault(id => _connectionByPlayer.TryGetValue(id, out var c) && c == connectionId);
            if (playerId is null)
            {
                // The player already reconnected on a newer connection.
                return OperationResult<DisconnectInfo>.Fail(ErrorCodes.NotInRoom);
            }

            _connectionByPlayer.TryRemove(new KeyValuePair<string, string>(playerId, connectionId));
            var now = _time.GetUtcNow();
            room.Disconnected.TryAdd(playerId, now);
            Touch(room, now);
            return OperationResult<DisconnectInfo>.Ok(
                new DisconnectInfo(BuildSnapshot(room, now), playerId, _options.DisconnectGraceSeconds));
        }, ct).ConfigureAwait(false);
    }

    public Task<OperationResult<RoomSnapshot>> GetRoomAsync(string roomCode, CancellationToken ct = default) =>
        WithRoomAsync(roomCode, room => OperationResult<RoomSnapshot>.Ok(BuildSnapshot(room, _time.GetUtcNow())), ct);

    // ───────────────────────── Background work ─────────────────────────

    public async Task<TimerReport> ProcessTimersAsync(CancellationToken ct = default)
    {
        List<RoomUpdate> ended = [];
        List<ReconnectCountdown> countdowns = [];
        List<PlayerDeparture> departures = [];
        var grace = TimeSpan.FromSeconds(_options.DisconnectGraceSeconds);

        foreach (var room in _rooms.Values.ToArray())
        {
            if (!await room.EnterAsync(ct).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                var now = _time.GetUtcNow();
                if (room.Status == RoomStatus.InProgress && room.Session is { Result: null } session
                    && room.TimeControl.IsTimed)
                {
                    var current = room.Engine.GetCurrentTurnPlayerId(session.State);
                    if (session.Remaining[current] - (now - session.TurnStartedAt) <= TimeSpan.Zero)
                    {
                        var timeout = room.Engine.DeclareTimeout(session.State, current);
                        EndGame(room, timeout, now);
                        Touch(room, now);
                        ended.Add(new RoomUpdate(BuildSnapshot(room, now), timeout));
                        continue;
                    }
                }

                foreach (var (playerId, since) in room.Disconnected.ToArray())
                {
                    if (room.Removed || room.Status == RoomStatus.Finished)
                    {
                        break;
                    }

                    var left = grace - (now - since);
                    if (left > TimeSpan.Zero)
                    {
                        countdowns.Add(new ReconnectCountdown(room.Code, playerId, (int)Math.Ceiling(left.TotalSeconds)));
                    }
                    else if (room.Status == RoomStatus.InProgress)
                    {
                        var forfeit = room.Engine.Resign(room.Session!.State, playerId) with
                        {
                            Reason = GameEndReason.Abandonment,
                        };
                        EndGame(room, forfeit, now);
                        Touch(room, now);
                        ended.Add(new RoomUpdate(BuildSnapshot(room, now), forfeit));
                    }
                    else
                    {
                        departures.Add(LeaveInternal(room, playerId, now));
                    }
                }
            }
            finally
            {
                room.Exit();
            }
        }

        return ended.Count + countdowns.Count + departures.Count == 0
            ? TimerReport.Empty
            : new TimerReport(ended, countdowns, departures);
    }

    public async Task<IReadOnlyList<string>> CleanupAsync(CancellationToken ct = default)
    {
        List<string> removed = [];
        foreach (var room in _rooms.Values.ToArray())
        {
            if (!IsStale(room, _time.GetUtcNow()) || !await room.EnterAsync(ct).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                if (IsStale(room, _time.GetUtcNow()))
                {
                    RemoveRoom(room);
                    removed.Add(room.Code);
                }
            }
            finally
            {
                room.Exit();
            }
        }

        return removed;
    }

    private bool IsStale(Room room, DateTimeOffset now)
    {
        var timeout = room.Status == RoomStatus.Finished
            ? TimeSpan.FromMinutes(_options.FinishedRoomMinutes)
            : TimeSpan.FromMinutes(_options.IdleRoomMinutes);
        return now - room.LastActivity > timeout;
    }

    // ───────────────────────── Internals (room lock held) ─────────────────────────

    private async Task<OperationResult<T>> WithRoomAsync<T>(
        string roomCode, Func<Room, OperationResult<T>> action, CancellationToken ct)
    {
        var code = RoomCodeGenerator.Normalize(roomCode);
        if (code.Length == 0 || !_rooms.TryGetValue(code, out var room)
            || !await room.EnterAsync(ct).ConfigureAwait(false))
        {
            return OperationResult<T>.Fail(ErrorCodes.RoomNotFound);
        }

        try
        {
            return action(room);
        }
        finally
        {
            room.Exit();
        }
    }

    /// <summary>Leaves a waiting/finished room the player is still seated in. Running games block.</summary>
    private async Task<string?> DetachFromOtherRoomAsync(string playerId, string? keepRoomCode, CancellationToken ct)
    {
        if (!_roomByPlayer.TryGetValue(playerId, out var code) || code == keepRoomCode)
        {
            return null;
        }

        var result = await WithRoomAsync(code, room =>
        {
            if (!IsMember(room, playerId))
            {
                return OperationResult<bool>.Ok(true);
            }

            if (room.Status == RoomStatus.InProgress)
            {
                return OperationResult<bool>.Fail(ErrorCodes.AlreadyInGame);
            }

            LeaveInternal(room, playerId, _time.GetUtcNow());
            return OperationResult<bool>.Ok(true);
        }, ct).ConfigureAwait(false);

        if (result.Success || result.ErrorCode == ErrorCodes.RoomNotFound)
        {
            _roomByPlayer.TryRemove(new KeyValuePair<string, string>(playerId, code));
            return null;
        }

        return result.ErrorCode;
    }

    private PlayerDeparture LeaveInternal(Room room, string playerId, DateTimeOffset now)
    {
        GameResult? newResult = null;
        if (room.Status == RoomStatus.InProgress && room.Session is { Result: null } session)
        {
            newResult = room.Engine.Resign(session.State, playerId);
            EndGame(room, newResult, now);
        }

        room.Players.RemoveAll(p => p.PlayerId == playerId);
        room.Ready.Remove(playerId);
        room.RematchVotes.Remove(playerId);
        room.Disconnected.Remove(playerId);
        ForgetPlayer(room.Code, playerId);

        if (room.Players.Count == 0)
        {
            RemoveRoom(room);
            return new PlayerDeparture(room.Code, playerId, null, newResult);
        }

        if (room.HostPlayerId == playerId)
        {
            room.HostPlayerId = room.Players.OrderBy(p => p.SeatNumber).First().PlayerId;
        }

        Touch(room, now);
        return new PlayerDeparture(room.Code, playerId, BuildSnapshot(room, now), newResult);
    }

    private void RemoveRoom(Room room)
    {
        room.Removed = true;
        _rooms.TryRemove(new KeyValuePair<string, Room>(room.Code, room));
        foreach (var player in room.Players)
        {
            ForgetPlayer(room.Code, player.PlayerId);
        }

        room.Players.Clear();
    }

    private void ForgetPlayer(string roomCode, string playerId)
    {
        _roomByPlayer.TryRemove(new KeyValuePair<string, string>(playerId, roomCode));
        if (_connectionByPlayer.TryRemove(playerId, out var connectionId))
        {
            _roomByConnection.TryRemove(new KeyValuePair<string, string>(connectionId, roomCode));
            _playerByConnection.TryRemove(connectionId, out _);
        }
    }

    private void RegisterConnection(string roomCode, string playerId, string connectionId)
    {
        if (_connectionByPlayer.TryGetValue(playerId, out var previous) && previous != connectionId)
        {
            _roomByConnection.TryRemove(previous, out _);
            _playerByConnection.TryRemove(previous, out _);
        }

        _playerByConnection[connectionId] = playerId;
        _connectionByPlayer[playerId] = connectionId;
        _roomByConnection[connectionId] = roomCode;
        _roomByPlayer[playerId] = roomCode;
    }

    private static bool IsMember(Room room, string playerId) =>
        playerId is not null && room.Players.Exists(p => p.PlayerId == playerId);

    private static void Touch(Room room, DateTimeOffset now) => room.LastActivity = now;

    private static string? RequireRunningGame(Room room, string playerId, out GameSession session)
    {
        session = room.Session!;
        if (!IsMember(room, playerId))
        {
            return ErrorCodes.NotInRoom;
        }

        if (room.Status == RoomStatus.Finished)
        {
            return ErrorCodes.GameOver;
        }

        return room.Status == RoomStatus.InProgress && room.Session is not null
            ? null
            : ErrorCodes.GameNotStarted;
    }

    private void StartSession(Room room, DateTimeOffset now)
    {
        var order = room.Players.OrderBy(p => p.SeatNumber).Select(p => p.PlayerId).ToList();
        if (room.GamesPlayed % 2 == 1)
        {
            order.Reverse(); // colors swap on every rematch
        }

        var session = new GameSession
        {
            State = room.Engine.CreateInitialState(order),
            PlayerIdsByColor = order,
            StateVersion = 1,
            StartedAt = now,
            TurnStartedAt = now,
        };

        if (room.TimeControl.IsTimed)
        {
            foreach (var id in order)
            {
                session.Remaining[id] = room.TimeControl.Initial;
            }
        }

        room.Session = session;
        room.Status = RoomStatus.InProgress;
        room.Ready.Clear();
        room.RematchVotes.Clear();
        Touch(room, now);
    }

    private static void EndGame(Room room, GameResult result, DateTimeOffset now)
    {
        var session = room.Session!;
        if (session.Result is not null)
        {
            return;
        }

        if (room.TimeControl.IsTimed)
        {
            var current = room.Engine.GetCurrentTurnPlayerId(session.State);
            var left = session.Remaining[current] - (now - session.TurnStartedAt);
            session.Remaining[current] = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        session.Result = result;
        session.EndedAt = now;
        session.DrawOfferedBy = null;
        room.Status = RoomStatus.Finished;
        room.GamesPlayed++;
        room.RematchVotes.Clear();
    }

    private RoomSnapshot BuildSnapshot(Room room, DateTimeOffset now)
    {
        var players = room.Players
            .OrderBy(p => p.SeatNumber)
            .Select(p => new PlayerSnapshot(
                p,
                room.Ready.Contains(p.PlayerId),
                !room.Disconnected.ContainsKey(p.PlayerId),
                p.PlayerId == room.HostPlayerId))
            .ToList();

        SessionSnapshot? sessionSnapshot = null;
        if (room.Session is { } s)
        {
            var current = room.Engine.GetCurrentTurnPlayerId(s.State);
            ClockSnapshot? clock = null;
            if (room.TimeControl.IsTimed)
            {
                var running = s.Result is null ? current : null;
                clock = new ClockSnapshot(
                    s.Remaining.ToDictionary(
                        kv => kv.Key,
                        kv =>
                        {
                            var left = kv.Value - (kv.Key == running ? now - s.TurnStartedAt : TimeSpan.Zero);
                            return left < TimeSpan.Zero ? 0L : (long)left.TotalMilliseconds;
                        }),
                    running,
                    now);
            }

            sessionSnapshot = new SessionSnapshot(
                s.State, s.StateVersion, s.PlayerIdsByColor, current, clock, s.DrawOfferedBy,
                s.Result, s.MoveCount, s.StartedAt, s.EndedAt);
        }

        return new RoomSnapshot(
            room.Code, room.Engine.GameTypeId, room.Status, room.TimeControl, room.HostPlayerId,
            room.Engine.MinPlayers, room.Engine.MaxPlayers, room.GamesPlayed, players,
            room.RematchVotes.ToArray(), sessionSnapshot);
    }

    // ───────────────────────── Validation ─────────────────────────

    private static bool TryBuildPlayer(
        string playerId, string displayName, string avatarId, int seat, out Player player)
    {
        player = null!;
        var name = displayName?.Trim() ?? string.Empty;
        if (!IsValidId(playerId, 64) || !IsValidId(avatarId, 32)
            || name.Length is < MinDisplayNameLength or > MaxDisplayNameLength
            || name.Any(char.IsControl))
        {
            return false;
        }

        player = new Player(playerId, name, avatarId, seat);
        return true;
    }

    private static bool IsValidId(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= maxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
