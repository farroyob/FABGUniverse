using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using FabGames.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FabGames.Core.Tests;

public sealed class RoomFixture
{
    public RoomFixture(Action<RoomManagerOptions>? configure = null)
    {
        var options = new RoomManagerOptions();
        configure?.Invoke(options);
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Manager = new RoomManager(
            new GameEngineRegistry([new NimEngine()]), Time, Options.Create(options));
    }

    public FakeTimeProvider Time { get; }

    public RoomManager Manager { get; }

    public async Task<RoomSnapshot> CreateAsync(
        string player = "alice", string connection = "c-alice", TimeControl? timeControl = null)
    {
        var result = await Manager.CreateRoomAsync(
            connection, "nim", player, player.ToUpperInvariant(), "av-1", timeControl ?? TimeControl.None);
        result.Success.Should().BeTrue(result.ErrorCode);
        return result.Value!.Snapshot;
    }

    public async Task<RoomUpdate> JoinAsync(string code, string player = "bob", string connection = "c-bob")
    {
        var result = await Manager.JoinRoomAsync(connection, code, player, player.ToUpperInvariant(), "av-2");
        result.Success.Should().BeTrue(result.ErrorCode);
        return result.Value!;
    }

    /// <summary>Creates a room with alice (first mover) and bob, both ready, game running.</summary>
    public async Task<string> StartedRoomAsync(TimeControl? timeControl = null)
    {
        var room = await CreateAsync(timeControl: timeControl);
        await JoinAsync(room.Code);
        (await Manager.SetReadyAsync(room.Code, "alice", true)).Success.Should().BeTrue();
        var started = await Manager.SetReadyAsync(room.Code, "bob", true);
        started.Value!.GameStarted.Should().BeTrue();
        return room.Code;
    }

    public async Task<RoomSnapshot> SnapshotAsync(string code) => (await Manager.GetRoomAsync(code)).Value!;

    public static string Move(int take) => $$"""{"take":{{take}}}""";
}
