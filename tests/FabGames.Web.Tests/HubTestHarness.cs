using System.Text.Json;
using FabGames.Web.DTOs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace FabGames.Web.Tests;

public sealed record HubEvent(string Name, object? Data);

/// <summary>An in-memory FAB Games server with a controllable clock.</summary>
public sealed class GameServer : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<TestClient> _clients = [];

    public GameServer()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);
            }));
    }

    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => _factory.Services;

    public async Task<TestClient> ConnectAsync()
    {
        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/game"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var client = new TestClient(connection, Guid.NewGuid().ToString("N"));
        _clients.Add(client);
        await connection.StartAsync();
        return client;
    }

    /// <summary>Opens a new connection that reuses another client's player id (a browser refresh).</summary>
    public async Task<TestClient> ReconnectAsync(TestClient previous)
    {
        var fresh = await ConnectAsync();
        fresh.PlayerId = previous.PlayerId;
        fresh.DisplayName = previous.DisplayName;
        return fresh;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }
}

public sealed class TestClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<HubEvent> _events = [];
    private readonly object _gate = new();

    public TestClient(HubConnection connection, string playerId)
    {
        Connection = connection;
        PlayerId = playerId;
        Listen<RoomViewDto>("StateUpdated");
        Listen<RoomViewDto>("GameStarted");
        Listen<RoomViewDto>("ReadyStateChanged");
        Listen<string, string>("ActionRejected");
        Listen<string, GameResultDto>("GameOver");
        Listen<string, string, int>("PlayerReconnecting");
        Listen<string, string>("PlayerReconnected");
        Listen<string, string>("DrawOffered");
        Listen<string, string>("DrawDeclined");
        Listen<string, string>("RematchRequested");
        Listen<string, ChatMessageDto>("ChatMessageReceived");
        Listen<string, PlayerDto>("PlayerJoinedRoom");
        Listen<string, string>("PlayerLeftRoom");
    }

    public HubConnection Connection { get; }

    public string PlayerId { get; set; }

    public string DisplayName { get; set; } = "Player";

    public string RoomCode { get; private set; } = string.Empty;

    /// <summary>Most recent view by state version.</summary>
    public RoomViewDto? View { get; private set; }

    public string Me => View?.YouId ?? string.Empty;

    private void Listen<T1>(string name) =>
        Connection.On<T1>(name, a => Record(name, a));

    private void Listen<T1, T2>(string name) =>
        Connection.On<T1, T2>(name, (a, b) => Record(name, (a, b)));

    private void Listen<T1, T2, T3>(string name) =>
        Connection.On<T1, T2, T3>(name, (a, b, c) => Record(name, (a, b, c)));

    private void Record(string name, object? data)
    {
        lock (_gate)
        {
            if (data is RoomViewDto view)
            {
                Adopt(view);
            }

            _events.Add(new HubEvent(name, data));
        }
    }

    private void Adopt(RoomViewDto view)
    {
        var current = View?.Session?.StateVersion ?? 0;
        var incoming = view.Session?.StateVersion ?? 0;
        if (View is null || incoming >= current)
        {
            View = view;
        }
    }

    // ── Hub calls ───────────────────────────────────────────────

    public async Task<HubResponse<RoomViewDto>> CreateRoomAsync(
        string gameTypeId, string? name = null, TimeControlDto? timeControl = null)
    {
        DisplayName = name ?? DisplayName;
        var response = await Connection.InvokeAsync<HubResponse<RoomViewDto>>(
            "CreateRoom", new CreateRoomRequest(gameTypeId, DisplayName, "av-1", PlayerId, timeControl));
        Adopt(response);
        return response;
    }

    public async Task<HubResponse<RoomViewDto>> JoinRoomAsync(string code, string? name = null)
    {
        DisplayName = name ?? DisplayName;
        var response = await Connection.InvokeAsync<HubResponse<RoomViewDto>>(
            "JoinRoom", new JoinRoomRequest(code, DisplayName, "av-2", PlayerId));
        Adopt(response);
        return response;
    }

    public async Task<HubResponse<RoomViewDto>> RejoinAsync(string code)
    {
        var response = await Connection.InvokeAsync<HubResponse<RoomViewDto>>("RejoinRoom", code, PlayerId);
        Adopt(response);
        return response;
    }

    private void Adopt(HubResponse<RoomViewDto> response)
    {
        if (response is { Ok: true, Data: { } view })
        {
            RoomCode = view.Room.Code;
            lock (_gate)
            {
                Adopt(view);
            }
        }
    }

    public Task<HubResponse> ReadyAsync(bool ready = true) =>
        Connection.InvokeAsync<HubResponse>("SetReady", RoomCode, ready);

    public Task<HubResponse> StartGameAsync() => Connection.InvokeAsync<HubResponse>("StartGame", RoomCode);

    public Task<HubResponse> MoveJsonAsync(string json, int? version = null) =>
        Connection.InvokeAsync<HubResponse>("MakeMove", RoomCode, json, version ?? View!.Session!.StateVersion);

    public Task<HubResponse> ChessMoveAsync(string from, string to, string? promotion = null, int? version = null) =>
        MoveJsonAsync(JsonSerializer.Serialize(new { from, to, promotion }, Json), version);

    public Task<HubResponse> CheckersMoveAsync(string from, params string[] path) =>
        MoveJsonAsync(JsonSerializer.Serialize(new { from, path }, Json));

    public Task<HubResponse> ResignAsync() => Connection.InvokeAsync<HubResponse>("Resign", RoomCode);

    public Task<HubResponse> OfferDrawAsync() => Connection.InvokeAsync<HubResponse>("OfferDraw", RoomCode);

    public Task<HubResponse> RespondDrawAsync(bool accept) =>
        Connection.InvokeAsync<HubResponse>("RespondDraw", RoomCode, accept);

    public Task<HubResponse> RematchAsync() => Connection.InvokeAsync<HubResponse>("RequestRematch", RoomCode);

    public Task<HubResponse> ChatAsync(string text) => Connection.InvokeAsync<HubResponse>("SendChat", RoomCode, text);

    public Task<HubResponse> LeaveAsync() => Connection.InvokeAsync<HubResponse>("LeaveRoom", RoomCode);

    // ── Observation ─────────────────────────────────────────────

    public IReadOnlyList<HubEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IEnumerable<T> Received<T>(string name) =>
        Events.Where(e => e.Name == name).Select(e => (T)e.Data!);

    public async Task<T> WaitForAsync<T>(string name, Func<T, bool>? predicate = null, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var e in Events)
            {
                if (e.Name == name && e.Data is T data && (predicate?.Invoke(data) ?? true))
                {
                    return data;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"{DisplayName} never received {name}. Got: {string.Join(", ", Events.Select(e => e.Name))}");
    }

    public Task<RoomViewDto> WaitForVersionAsync(int version) =>
        WaitForAsync<RoomViewDto>(
            "StateUpdated", v => v.Session is not null && v.Session.StateVersion >= version);

    public ChessStateDto Chess() => Game<ChessStateDto>();

    public CheckersStateDto Checkers() => Game<CheckersStateDto>();

    private T Game<T>() =>
        JsonSerializer.Deserialize<T>(((JsonElement)View!.Session!.Game).GetRawText(), Json)!;

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
