using FabGames.Core.Interfaces;
using FabGames.Web.Hubs;

namespace FabGames.Web.BackgroundServices;

/// <summary>
/// Owns the game clocks and the disconnect grace period: declares timeouts, forfeits abandoned games
/// and pushes reconnection countdowns to the opponent.
/// </summary>
public sealed class TurnClockService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly IRoomManager _rooms;
    private readonly RoomBroadcaster _broadcaster;
    private readonly TimeProvider _time;
    private readonly ILogger<TurnClockService> _logger;
    private readonly Dictionary<(string Room, string Player), int> _lastCountdown = [];

    public TurnClockService(
        IRoomManager rooms, RoomBroadcaster broadcaster, TimeProvider time, ILogger<TurnClockService> logger)
    {
        _rooms = rooms;
        _broadcaster = broadcaster;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One sweep; public so tests can drive it deterministically.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var report = await _rooms.ProcessTimersAsync(ct).ConfigureAwait(false);

            foreach (var ended in report.Ended)
            {
                await _broadcaster.UpdateAsync(ended).ConfigureAwait(false);
            }

            foreach (var departure in report.Departures)
            {
                await _broadcaster.PlayerLeftAsync(departure).ConfigureAwait(false);
            }

            var live = new HashSet<(string, string)>();
            foreach (var countdown in report.Countdowns)
            {
                var key = (countdown.RoomCode, countdown.PlayerId);
                live.Add(key);
                if (!_lastCountdown.TryGetValue(key, out var last) || last != countdown.SecondsLeft)
                {
                    _lastCountdown[key] = countdown.SecondsLeft;
                    await _broadcaster.PlayerReconnectingAsync(
                        countdown.RoomCode, countdown.PlayerId, countdown.SecondsLeft).ConfigureAwait(false);
                }
            }

            foreach (var stale in _lastCountdown.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _lastCountdown.Remove(stale);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed sweep must not kill the clock; the next tick retries.
            _logger.LogError(ex, "Timer sweep failed");
        }
    }
}

/// <summary>Periodically removes idle and long-finished rooms.</summary>
public sealed class RoomCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IRoomManager _rooms;
    private readonly TimeProvider _time;
    private readonly ILogger<RoomCleanupService> _logger;

    public RoomCleanupService(IRoomManager rooms, TimeProvider time, ILogger<RoomCleanupService> logger)
    {
        _rooms = rooms;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var removed = await _rooms.CleanupAsync(ct).ConfigureAwait(false);
            if (removed.Count > 0)
            {
                _logger.LogInformation("Removed {Count} idle rooms", removed.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Room cleanup failed");
        }
    }
}
