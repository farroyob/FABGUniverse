using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;

namespace FabGames.Core.Domain.Models;

/// <summary>Live state of one running game inside a room. Mutated only under the room lock.</summary>
internal sealed class GameSession
{
    public required object State { get; set; }

    public int StateVersion { get; set; }

    /// <summary>Player ids indexed by <c>PlayerColor</c>.</summary>
    public required IReadOnlyList<string> PlayerIdsByColor { get; init; }

    /// <summary>Remaining time per player, as of <see cref="TurnStartedAt"/>. Empty for untimed games.</summary>
    public Dictionary<string, TimeSpan> Remaining { get; } = [];

    public DateTimeOffset TurnStartedAt { get; set; }

    public string? DrawOfferedBy { get; set; }

    public GameResult? Result { get; set; }

    public int MoveCount { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// Mutable room state. All access happens between <see cref="EnterAsync"/> and <see cref="Exit"/>.
/// The semaphore is disposed once the room was removed and the last user left.
/// </summary>
internal sealed class Room
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _users;
    private int _disposed;
    private long _lastActivityTicks;

    public required string Code { get; init; }

    public required IGameEngine Engine { get; init; }

    public required TimeControl TimeControl { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string HostPlayerId { get; set; } = string.Empty;

    public Enums.RoomStatus Status { get; set; } = Enums.RoomStatus.Waiting;

    public List<Player> Players { get; } = [];

    public HashSet<string> Ready { get; } = [];

    public HashSet<string> RematchVotes { get; } = [];

    /// <summary>Players currently without a connection, with the moment they dropped.</summary>
    public Dictionary<string, DateTimeOffset> Disconnected { get; } = [];

    public GameSession? Session { get; set; }

    public int GamesPlayed { get; set; }

    public volatile bool Removed;

    public DateTimeOffset LastActivity
    {
        get => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastActivityTicks, value.UtcTicks);
    }

    /// <summary>Takes the room lock. Returns false (without holding it) if the room was removed.</summary>
    public async Task<bool> EnterAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _users);
        if (Removed)
        {
            Leave();
            return false;
        }

        try
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Leave();
            throw;
        }

        if (!Removed)
        {
            return true;
        }

        _lock.Release();
        Leave();
        return false;
    }

    public void Exit()
    {
        _lock.Release();
        Leave();
    }

    private void Leave()
    {
        if (Interlocked.Decrement(ref _users) == 0 && Removed
            && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lock.Dispose();
        }
    }
}
