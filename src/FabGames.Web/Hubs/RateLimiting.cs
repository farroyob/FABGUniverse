using System.Collections.Concurrent;
using FabGames.Core.Domain.Results;
using Microsoft.AspNetCore.SignalR;

namespace FabGames.Web.Hubs;

public sealed class HubRateLimitOptions
{
    public const string SectionName = "HubRateLimit";

    /// <summary>Messages a connection may send in a burst.</summary>
    public double Burst { get; set; } = 30;

    /// <summary>Sustained messages per second.</summary>
    public double RefillPerSecond { get; set; } = 10;
}

/// <summary>Token bucket per connection.</summary>
public sealed class ConnectionRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly HubRateLimitOptions _options;
    private readonly TimeProvider _time;

    public ConnectionRateLimiter(Microsoft.Extensions.Options.IOptions<HubRateLimitOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public bool TryAcquire(string connectionId, double cost = 1)
    {
        var bucket = _buckets.GetOrAdd(connectionId, _ => new Bucket(_options.Burst, _time.GetTimestamp()));
        lock (bucket)
        {
            var now = _time.GetTimestamp();
            var elapsed = _time.GetElapsedTime(bucket.LastRefill, now).TotalSeconds;
            bucket.LastRefill = now;
            bucket.Tokens = Math.Min(_options.Burst, bucket.Tokens + (elapsed * _options.RefillPerSecond));
            if (bucket.Tokens < cost)
            {
                return false;
            }

            bucket.Tokens -= cost;
            return true;
        }
    }

    public void Forget(string connectionId) => _buckets.TryRemove(connectionId, out _);

    private sealed class Bucket(double tokens, long lastRefill)
    {
        public double Tokens = tokens;
        public long LastRefill = lastRefill;
    }
}

/// <summary>Rejects hub calls from connections that send too fast. Chat and room creation cost more.</summary>
public sealed class RateLimitHubFilter : IHubFilter
{
    private readonly ConnectionRateLimiter _limiter;

    public RateLimitHubFilter(ConnectionRateLimiter limiter) => _limiter = limiter;

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var cost = invocationContext.HubMethodName switch
        {
            nameof(GameHub.SendChat) => 4,
            nameof(GameHub.CreateRoom) or nameof(GameHub.JoinRoom) => 5,
            _ => 1,
        };

        if (!_limiter.TryAcquire(invocationContext.Context.ConnectionId, cost))
        {
            throw new HubException(ErrorCodes.RateLimited);
        }

        return next(invocationContext);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        _limiter.Forget(context.Context.ConnectionId);
        return next(context, exception);
    }
}
