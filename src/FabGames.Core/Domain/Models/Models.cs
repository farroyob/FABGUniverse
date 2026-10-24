using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;

namespace FabGames.Core.Domain.Models;

/// <summary>Stable player identity. The connection id is tracked separately by the room manager.</summary>
public sealed record Player(string PlayerId, string DisplayName, string AvatarId, int SeatNumber);

/// <summary>Per-player clock. <c>InitialMinutes == 0</c> means untimed.</summary>
public sealed record TimeControl(int InitialMinutes, int IncrementSeconds)
{
    public static readonly int[] AllowedMinutes = [0, 5, 10, 15];
    public const int MaxIncrementSeconds = 30;

    public static TimeControl None { get; } = new(0, 0);

    public bool IsTimed => InitialMinutes > 0;

    public TimeSpan Initial => TimeSpan.FromMinutes(InitialMinutes);

    public TimeSpan Increment => TimeSpan.FromSeconds(IncrementSeconds);

    public bool IsValid =>
        Array.IndexOf(AllowedMinutes, InitialMinutes) >= 0
        && IncrementSeconds is >= 0 and <= MaxIncrementSeconds
        && (IsTimed || IncrementSeconds == 0);
}

public sealed record PlayerSnapshot(Player Player, bool IsReady, bool IsConnected, bool IsHost);

/// <summary>
/// Clock readings are already net of elapsed time at <see cref="ServerTime"/>. Only
/// <see cref="RunningPlayerId"/> has a ticking clock; clients count down locally from receipt.
/// </summary>
public sealed record ClockSnapshot(
    IReadOnlyDictionary<string, long> RemainingMs,
    string? RunningPlayerId,
    DateTimeOffset ServerTime);

public sealed record SessionSnapshot(
    object State,
    int StateVersion,
    IReadOnlyList<string> PlayerIdsByColor,
    string CurrentTurnPlayerId,
    ClockSnapshot? Clock,
    string? DrawOfferedBy,
    GameResult? Result,
    int MoveCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

/// <summary>Immutable view of a room, safe to use outside the room lock.</summary>
public sealed record RoomSnapshot(
    string Code,
    string GameTypeId,
    RoomStatus Status,
    TimeControl TimeControl,
    string HostPlayerId,
    int MinPlayers,
    int MaxPlayers,
    int GamesPlayed,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyCollection<string> RematchVotes,
    SessionSnapshot? Session);

/// <summary>Outcome of a successful room mutation.</summary>
/// <param name="Snapshot">Room state after the mutation.</param>
/// <param name="NewResult">Set only when this very operation ended the game.</param>
/// <param name="GameStarted">True when this operation started a (new) game.</param>
public sealed record RoomUpdate(RoomSnapshot Snapshot, GameResult? NewResult = null, bool GameStarted = false);

public sealed record DisconnectInfo(RoomSnapshot Snapshot, string PlayerId, int GraceSeconds);

public sealed record ChatMessage(string PlayerId, string DisplayName, string Text, DateTimeOffset SentAt);

public sealed record ReconnectCountdown(string RoomCode, string PlayerId, int SecondsLeft);

/// <param name="RoomCode">Room the player left.</param>
/// <param name="PlayerId">Player who was removed.</param>
/// <param name="Snapshot">Room after the departure, or null if the room was deleted.</param>
/// <param name="NewResult">Set when leaving ended a running game (the leaver forfeited).</param>
public sealed record PlayerDeparture(
    string RoomCode, string PlayerId, RoomSnapshot? Snapshot, GameResult? NewResult = null);

public sealed record TimerReport(
    IReadOnlyList<RoomUpdate> Ended,
    IReadOnlyList<ReconnectCountdown> Countdowns,
    IReadOnlyList<PlayerDeparture> Departures)
{
    public static TimerReport Empty { get; } = new([], [], []);
}
