using System.Text.Json;
using FabGames.Core.Domain.Results;
using FabGames.Web.BackgroundServices;
using FabGames.Web.DTOs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace FabGames.Web.Tests;

public class HubIntegrationTests
{
    private static async Task<(TestClient White, TestClient Black, string Code)> StartGameAsync(
        GameServer server, string game = "chess", TimeControlDto? timeControl = null)
    {
        var host = await server.ConnectAsync();
        var guest = await server.ConnectAsync();

        var created = await host.CreateRoomAsync(game, "Hosty", timeControl);
        created.Ok.Should().BeTrue(created.Error);
        var code = created.Data!.Room.Code;

        var joined = await guest.JoinRoomAsync(code.ToLowerInvariant(), "Guesty");
        joined.Ok.Should().BeTrue(joined.Error);
        await host.WaitForAsync<(string, PlayerDto)>("PlayerJoinedRoom", e => e.Item2.DisplayName == "Guesty");

        (await host.ReadyAsync()).Ok.Should().BeTrue();
        (await guest.ReadyAsync()).Ok.Should().BeTrue();
        await host.WaitForAsync<RoomViewDto>("GameStarted");
        await guest.WaitForAsync<RoomViewDto>("GameStarted");
        return (host, guest, code);
    }

    // ── Foundations ───────────────────────────────────────────

    [Fact]
    public async Task TwoPlayers_CreateJoinAndPlayFoolsMate_ToCheckmate()
    {
        await using var server = new GameServer();
        var (white, black, code) = await StartGameAsync(server);

        white.View!.Session!.StateVersion.Should().Be(1);
        white.View.Session.ColorOrder.Should().Equal(white.Me, black.Me);
        white.Chess().LegalMoves.Should().HaveCount(20, "the player to move receives their legal moves");
        black.Chess().LegalMoves.Should().BeEmpty("the waiting player receives none");

        (await white.ChessMoveAsync("f2", "f3")).Ok.Should().BeTrue();
        await black.WaitForVersionAsync(2);
        black.Chess().LegalMoves.Should().HaveCount(20);
        (await black.ChessMoveAsync("e7", "e5")).Ok.Should().BeTrue();
        await white.WaitForVersionAsync(3);
        (await white.ChessMoveAsync("g2", "g4")).Ok.Should().BeTrue();
        await black.WaitForVersionAsync(4);
        (await black.ChessMoveAsync("d8", "h4")).Ok.Should().BeTrue();

        var over = await white.WaitForAsync<(string Code, GameResultDto Result)>("GameOver");
        over.Code.Should().Be(code);
        over.Result.Outcome.Should().Be("win");
        over.Result.WinnerId.Should().Be(black.Me);
        over.Result.Reason.Should().Be("Checkmate");
        (await black.WaitForAsync<(string, GameResultDto)>("GameOver")).Item2.Reason.Should().Be("Checkmate");

        var final = await white.WaitForVersionAsync(5);
        final.Room.Status.Should().Be("finished");
        final.Session!.Result!.WinnerId.Should().Be(black.Me);
        white.Chess().Moves.Select(m => m.San).Should().Equal("f3", "e5", "g4", "Qh4#");
        white.Chess().InCheck.Should().BeTrue();
        white.Chess().CheckSquare.Should().Be("e1");
    }

    [Fact]
    public async Task SecretPlayerIds_NeverLeaveTheServer()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);
        await white.ChessMoveAsync("e2", "e4");
        await black.WaitForVersionAsync(2);
        await white.ChatAsync("hello");
        await black.WaitForAsync<(string, ChatMessageDto)>("ChatMessageReceived");

        var everything = string.Join('\n', white.Events.Concat(black.Events).Select(e => JsonSerializer.Serialize(e.Data)));
        everything.Should().NotContain(white.PlayerId).And.NotContain(black.PlayerId);
        white.Me.Should().NotBe(white.PlayerId).And.HaveLength(16);
    }

    // ── Rejections ────────────────────────────────────────────

    [Fact]
    public async Task IllegalMove_IsRejected_AndTheBoardIsUnchanged()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);

        var response = await white.ChessMoveAsync("e2", "e5");
        response.Ok.Should().BeFalse();
        response.Error.Should().Be(ErrorCodes.IllegalMove);
        (await white.WaitForAsync<(string, string)>("ActionRejected")).Item2.Should().Be(ErrorCodes.IllegalMove);

        white.View!.Session!.StateVersion.Should().Be(1);
        white.Chess().Board[12].Should().Be("wP", "the pawn is still on e2");
        white.Chess().Board[36].Should().BeEmpty();
        black.Events.Should().NotContain(e => e.Name == "StateUpdated", "the opponent never saw a thing");
    }

    [Fact]
    public async Task OutOfTurn_Stale_AndMalformedMoves_AreRejectedWithCodes()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);

        (await black.ChessMoveAsync("e7", "e5")).Error.Should().Be(ErrorCodes.NotYourTurn);
        (await white.ChessMoveAsync("e2", "e4", version: 9)).Error.Should().Be(ErrorCodes.StaleState);
        (await white.MoveJsonAsync("not json")).Error.Should().Be(ErrorCodes.InvalidAction);
        (await white.ChessMoveAsync("e2", "e4", promotion: "k")).Error.Should().Be(ErrorCodes.InvalidAction);
        (await white.ChessMoveAsync("e2", "e4")).Ok.Should().BeTrue();
        (await white.ChessMoveAsync("d2", "d4", version: 2)).Error.Should().Be(ErrorCodes.NotYourTurn);
    }

    [Fact]
    public async Task InvalidRequests_AreRefusedWithoutThrowing()
    {
        await using var server = new GameServer();
        var client = await server.ConnectAsync();

        var shortId = await client.Connection.InvokeAsync<HubResponse<RoomViewDto>>(
            "CreateRoom", new CreateRoomRequest("chess", "Name", "av", "short", null));
        shortId.Error.Should().Be(ErrorCodes.InvalidRequest);

        var nullRequest = await client.Connection.InvokeAsync<HubResponse<RoomViewDto>>("CreateRoom", (CreateRoomRequest?)null);
        nullRequest.Error.Should().Be(ErrorCodes.InvalidRequest);

        (await client.CreateRoomAsync("go")).Error.Should().Be(ErrorCodes.UnknownGame);
        (await client.CreateRoomAsync("chess", "x")).Error.Should().Be(ErrorCodes.InvalidRequest);
        (await client.JoinRoomAsync("ZZZZZZ", "Valid")).Error.Should().Be(ErrorCodes.RoomNotFound);
        (await client.ResignAsync()).Error.Should().Be(ErrorCodes.NotInRoom);
    }

    [Fact]
    public async Task ThirdPlayer_CannotJoinAFullRoom()
    {
        await using var server = new GameServer();
        var (_, _, code) = await StartGameAsync(server);
        var intruder = await server.ConnectAsync();
        (await intruder.JoinRoomAsync(code)).Error.Should().Be(ErrorCodes.GameInProgress);
    }

    [Fact]
    public async Task Flooding_TheHub_IsRateLimited()
    {
        await using var server = new GameServer();
        var client = await server.ConnectAsync();

        var failures = 0;
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await client.ResignAsync();
            }
            catch (HubException ex) when (ex.Message.Contains(ErrorCodes.RateLimited, StringComparison.Ordinal))
            {
                failures++;
            }
        }

        failures.Should().BeGreaterThan(20, "30 calls fit in the burst, the rest are refused");
        server.Time.Advance(TimeSpan.FromSeconds(5));
        (await client.ResignAsync()).Error.Should().Be(ErrorCodes.NotInRoom, "tokens refill over time");
    }

    // ── Social features ───────────────────────────────────────

    [Fact]
    public async Task Chat_IsBroadcastAsPlainText_AndLimitedTo200Characters()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);

        (await white.ChatAsync("  hi <b>there</b> \n")).Ok.Should().BeTrue();
        var message = (await black.WaitForAsync<(string, ChatMessageDto)>("ChatMessageReceived")).Item2;
        message.Text.Should().Be("hi <b>there</b>", "markup is delivered verbatim as text; clients must not render it as HTML");
        message.DisplayName.Should().Be("Hosty");
        message.PlayerId.Should().Be(white.Me);

        (await white.ChatAsync(new string('x', 201))).Error.Should().Be(ErrorCodes.MessageInvalid);
        (await white.ChatAsync("   ")).Error.Should().Be(ErrorCodes.MessageInvalid);
    }

    [Fact]
    public async Task DrawOffer_CanBeDeclined_ThenAccepted()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);

        (await white.OfferDrawAsync()).Ok.Should().BeTrue();
        (await black.WaitForAsync<(string, string)>("DrawOffered")).Item2.Should().Be(white.Me);
        (await black.WaitForVersionAsync(1)).Session!.DrawOfferedBy.Should().Be(white.Me);

        (await black.RespondDrawAsync(false)).Ok.Should().BeTrue();
        (await white.WaitForAsync<(string, string)>("DrawDeclined")).Item2.Should().Be(black.Me);

        (await black.OfferDrawAsync()).Ok.Should().BeTrue();
        (await white.RespondDrawAsync(true)).Ok.Should().BeTrue();
        var over = await black.WaitForAsync<(string, GameResultDto)>("GameOver");
        over.Item2.Outcome.Should().Be("draw");
        over.Item2.Reason.Should().Be("DrawAgreement");
    }

    [Fact]
    public async Task Resign_EndsTheGame_ForBoth()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);
        (await black.ResignAsync()).Ok.Should().BeTrue();
        var over = await white.WaitForAsync<(string, GameResultDto)>("GameOver");
        over.Item2.WinnerId.Should().Be(white.Me);
        over.Item2.Reason.Should().Be("Resignation");
    }

    [Fact]
    public async Task Rematch_StartsANewGame_WithSwappedColors()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);
        await black.ResignAsync();
        await white.WaitForAsync<(string, GameResultDto)>("GameOver");

        (await white.RematchAsync()).Ok.Should().BeTrue();
        (await black.WaitForAsync<(string, string)>("RematchRequested")).Item2.Should().Be(white.Me);
        (await black.RematchAsync()).Ok.Should().BeTrue();

        var restarted = await white.WaitForAsync<RoomViewDto>("GameStarted", v => v.Room.GamesPlayed == 1);
        restarted.Session!.ColorOrder.Should().Equal(black.Me, white.Me);
        restarted.Session.StateVersion.Should().Be(1);
        restarted.Session.CurrentTurnPlayerId.Should().Be(black.Me);
        restarted.Room.Status.Should().Be("inprogress");

        (await black.WaitForAsync<RoomViewDto>("GameStarted", v => v.Room.GamesPlayed == 1))
            .Session!.Game.Should().NotBeNull();
        black.Chess().LegalMoves.Should().HaveCount(20, "black now plays the first move");
    }

    // ── Reconnection ──────────────────────────────────────────

    [Fact]
    public async Task Refresh_MidGame_ReattachesToTheSameSeat()
    {
        await using var server = new GameServer();
        var (white, black, code) = await StartGameAsync(server);
        await white.ChessMoveAsync("e2", "e4");
        await black.WaitForVersionAsync(2);

        await black.Connection.StopAsync();
        var countdown = await white.WaitForAsync<(string, string, int)>("PlayerReconnecting");
        countdown.Item2.Should().Be(black.Me);
        countdown.Item3.Should().Be(60);

        var refreshed = await server.ReconnectAsync(black);
        var rejoin = await refreshed.RejoinAsync(code);
        rejoin.Ok.Should().BeTrue(rejoin.Error);
        rejoin.Data!.YouId.Should().Be(black.Me);
        rejoin.Data.Session!.StateVersion.Should().Be(2);
        refreshed.Chess().Board[28].Should().Be("wP", "the position survived the refresh");
        refreshed.Chess().LegalMoves.Should().HaveCount(20, "it is still black's turn");

        await white.WaitForAsync<(string, string)>("PlayerReconnected");
        (await refreshed.ChessMoveAsync("e7", "e5")).Ok.Should().BeTrue();
        await white.WaitForVersionAsync(3);

        // The abandoned socket's late disconnect must not forfeit the seat.
        server.Time.Advance(TimeSpan.FromMinutes(2));
        await server.Services.GetRequiredService<TurnClockService>().TickAsync(default);
        white.Events.Should().NotContain(e => e.Name == "GameOver");
    }

    [Fact]
    public async Task Disconnect_PastTheGracePeriod_ForfeitsTheGame()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server);
        var clock = server.Services.GetRequiredService<TurnClockService>();

        await black.Connection.StopAsync();
        await white.WaitForAsync<(string, string, int)>("PlayerReconnecting", e => e.Item3 == 60);

        server.Time.Advance(TimeSpan.FromSeconds(20));
        await clock.TickAsync(default);
        (await white.WaitForAsync<(string, string, int)>("PlayerReconnecting", e => e.Item3 == 40)).Item2.Should().Be(black.Me);

        server.Time.Advance(TimeSpan.FromSeconds(41));
        await clock.TickAsync(default);
        var over = await white.WaitForAsync<(string, GameResultDto)>("GameOver");
        over.Item2.WinnerId.Should().Be(white.Me);
        over.Item2.Reason.Should().Be("Abandonment");
    }

    // ── Clocks ────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_IsDeclaredByTheServerClock()
    {
        await using var server = new GameServer();
        var (white, black, _) = await StartGameAsync(server, timeControl: new TimeControlDto(5, 0));
        white.View!.Session!.Clock!.RemainingMs[white.Me].Should().Be(300_000);
        white.View.Session.Clock.RunningPlayerId.Should().Be(white.Me);

        server.Time.Advance(TimeSpan.FromMinutes(5));
        await server.Services.GetRequiredService<TurnClockService>().TickAsync(default);

        var over = await black.WaitForAsync<(string, GameResultDto)>("GameOver");
        over.Item2.WinnerId.Should().Be(black.Me);
        over.Item2.Reason.Should().Be("Timeout");
        (await white.ChessMoveAsync("e2", "e4", version: 1)).Error.Should().Be(ErrorCodes.GameOver);
    }

    // ── Checkers over the wire ────────────────────────────────

    [Fact]
    public async Task Checkers_ForcedCapture_IsEnforced_AndExposedToTheClient()
    {
        await using var server = new GameServer();
        var (light, dark, _) = await StartGameAsync(server, "checkers");

        // Index 0 (the host) plays light; dark moves first.
        dark.Checkers().LegalMoves.Should().HaveCount(7);
        light.Checkers().LegalMoves.Should().BeEmpty();
        (await light.CheckersMoveAsync("c3", "d4")).Error.Should().Be(ErrorCodes.NotYourTurn);

        (await dark.CheckersMoveAsync("f6", "e5")).Ok.Should().BeTrue();
        await light.WaitForVersionAsync(2);
        (await light.CheckersMoveAsync("c3", "d4")).Ok.Should().BeTrue();
        await dark.WaitForVersionAsync(3);

        dark.Checkers().MustCapture.Should().BeTrue();
        dark.Checkers().LegalMoves.Should().ContainSingle().Which.Path.Should().Equal("c3");
        (await dark.CheckersMoveAsync("e5", "f4")).Error.Should().Be(ErrorCodes.MandatoryCapture);
        (await dark.CheckersMoveAsync("e5", "c3")).Ok.Should().BeTrue();

        var after = await light.WaitForVersionAsync(4);
        after.Session!.Result.Should().BeNull();
        light.Checkers().Moves.Select(m => m.Notation).Should().Equal("f6-e5", "c3-d4", "e5xc3");
        light.Checkers().MustCapture.Should().BeTrue("light must retake");
        light.Checkers().LastMove!.Captured.Should().Equal("d4");
    }
}
