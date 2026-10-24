using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Models;
using FabGames.Core.Domain.Results;
using FabGames.Core.Services;

namespace FabGames.Core.Tests;

public class RoomManagerTests
{
    // ── Creation and joining ──────────────────────────────────

    [Fact]
    public async Task CreateRoom_ReturnsAShareableCode_AndMakesTheCreatorHost()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        room.Code.Should().HaveLength(6).And.MatchRegex("^[A-HJKMNP-Z2-9]{6}$");
        room.Status.Should().Be(RoomStatus.Waiting);
        room.HostPlayerId.Should().Be("alice");
        room.Players.Should().ContainSingle().Which.Player.SeatNumber.Should().Be(0);
        fx.Manager.GetRoomCodeOfPlayer("alice").Should().Be(room.Code);
        fx.Manager.GetConnectionId("alice").Should().Be("c-alice");
    }

    [Fact]
    public void RoomCodes_NeverContainLookAlikeCharacters()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => RoomCodeGenerator.Generate());
        char[] lookAlikes = ['0', 'O', '1', 'I', 'L'];
        codes.Should().OnlyContain(c => c.Length == 6 && c.IndexOfAny(lookAlikes) < 0);
        RoomCodeGenerator.Normalize(" abc234 ").Should().Be("ABC234");
        RoomCodeGenerator.Normalize("ABC0O1").Should().BeEmpty();
        RoomCodeGenerator.Normalize("SHORT").Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Alice", "av")]
    [InlineData("alice", "A", "av")]
    [InlineData("alice", "ThisNameIsFarTooLong", "av")]
    [InlineData("alice", "Al\nice", "av")]
    [InlineData("alice", "Alice", "")]
    [InlineData("alice", "Alice", "<script>")]
    [InlineData("bad id", "Alice", "av")]
    public async Task CreateRoom_RejectsInvalidPlayers(string playerId, string name, string avatar)
    {
        var fx = new RoomFixture();
        var result = await fx.Manager.CreateRoomAsync("c1", "nim", playerId, name, avatar, TimeControl.None);
        result.ErrorCode.Should().Be(ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task CreateRoom_RejectsUnknownGames_AndBadTimeControls()
    {
        var fx = new RoomFixture();
        (await fx.Manager.CreateRoomAsync("c1", "go", "alice", "Alice", "av", TimeControl.None))
            .ErrorCode.Should().Be(ErrorCodes.UnknownGame);
        (await fx.Manager.CreateRoomAsync("c1", "nim", "alice", "Alice", "av", new TimeControl(7, 0)))
            .ErrorCode.Should().Be(ErrorCodes.InvalidRequest);
        (await fx.Manager.CreateRoomAsync("c1", "nim", "alice", "Alice", "av", new TimeControl(5, 99)))
            .ErrorCode.Should().Be(ErrorCodes.InvalidRequest);
        (await fx.Manager.CreateRoomAsync("c1", "nim", "alice", "Alice", "av", new TimeControl(0, 5)))
            .ErrorCode.Should().Be(ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task CreateRoom_RespectsTheRoomLimit()
    {
        var fx = new RoomFixture(o => o.MaxRooms = 2);
        await fx.CreateAsync("p1", "c1");
        await fx.CreateAsync("p2", "c2");
        (await fx.Manager.CreateRoomAsync("c3", "nim", "p3", "Third", "av", TimeControl.None))
            .ErrorCode.Should().Be(ErrorCodes.RoomLimitReached);
    }

    [Fact]
    public async Task JoinRoom_IsCaseInsensitive_AndFillsTheSeat()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        var joined = await fx.JoinAsync(room.Code.ToLowerInvariant());
        joined.Snapshot.Players.Select(p => p.Player.PlayerId).Should().Equal("alice", "bob");
        joined.Snapshot.Players[1].Player.SeatNumber.Should().Be(1);
    }

    [Fact]
    public async Task JoinRoom_UnknownOrMalformedCodes_AreNotFound()
    {
        var fx = new RoomFixture();
        foreach (var code in new[] { "ZZZZZZ", "nope", "", "ABC0O1" })
        {
            (await fx.Manager.JoinRoomAsync("c2", code, "bob", "Bob", "av"))
                .ErrorCode.Should().Be(ErrorCodes.RoomNotFound, code);
        }
    }

    [Fact]
    public async Task JoinRace_ForTheLastSeat_HasExactlyOneWinner()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();

        var attempts = Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            fx.Manager.JoinRoomAsync($"conn-{i}", room.Code, $"racer{i}", $"Racer{i}", "av"))).ToArray();
        var results = await Task.WhenAll(attempts);

        results.Count(r => r.Success).Should().Be(1);
        results.Where(r => !r.Success).Should().OnlyContain(r => r.ErrorCode == ErrorCodes.RoomFull);
        (await fx.SnapshotAsync(room.Code)).Players.Should().HaveCount(2);
    }

    [Fact]
    public async Task JoinRoom_ByAnExistingPlayer_IsARejoin_NotASecondSeat()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        var again = await fx.Manager.JoinRoomAsync("c-new", room.Code, "alice", "Alice", "av-1");
        again.Success.Should().BeTrue();
        again.Value!.Snapshot.Players.Should().ContainSingle();
        fx.Manager.GetConnectionId("alice").Should().Be("c-new");
    }

    [Fact]
    public async Task JoinRoom_AfterTheGameStarted_IsRefused()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        (await fx.Manager.JoinRoomAsync("c3", code, "carol", "Carol", "av"))
            .ErrorCode.Should().Be(ErrorCodes.GameInProgress);
    }

    [Fact]
    public async Task CreatingAnotherRoom_LeavesTheOldWaitingRoom_ButNotARunningGame()
    {
        var fx = new RoomFixture();
        var first = await fx.CreateAsync();
        var second = await fx.CreateAsync(connection: "c-alice-2");
        second.Code.Should().NotBe(first.Code);
        (await fx.Manager.GetRoomAsync(first.Code)).ErrorCode.Should().Be(ErrorCodes.RoomNotFound, "the empty room is gone");

        var fx2 = new RoomFixture();
        await fx2.StartedRoomAsync();
        (await fx2.Manager.CreateRoomAsync("c-x", "nim", "alice", "Alice", "av", TimeControl.None))
            .ErrorCode.Should().Be(ErrorCodes.AlreadyInGame);
    }

    // ── Ready and start ───────────────────────────────────────

    [Fact]
    public async Task Game_StartsWhenEverySeatIsFilledAndReady()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        await fx.JoinAsync(room.Code);
        var first = await fx.Manager.SetReadyAsync(room.Code, "alice", true);
        first.Value!.GameStarted.Should().BeFalse();
        first.Value.Snapshot.Players[0].IsReady.Should().BeTrue();

        var second = await fx.Manager.SetReadyAsync(room.Code, "bob", true);
        second.Value!.GameStarted.Should().BeTrue();
        var snapshot = second.Value.Snapshot;
        snapshot.Status.Should().Be(RoomStatus.InProgress);
        snapshot.Session!.StateVersion.Should().Be(1);
        snapshot.Session.PlayerIdsByColor.Should().Equal("alice", "bob");
        snapshot.Session.CurrentTurnPlayerId.Should().Be("alice");
        snapshot.Players.Should().OnlyContain(p => !p.IsReady, "ready flags reset when the game starts");
    }

    [Fact]
    public async Task ReadyCanBeToggledOff_AndNonMembersAreRefused()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        await fx.JoinAsync(room.Code);
        await fx.Manager.SetReadyAsync(room.Code, "alice", true);
        var off = await fx.Manager.SetReadyAsync(room.Code, "alice", false);
        off.Value!.Snapshot.Players[0].IsReady.Should().BeFalse();
        (await fx.Manager.SetReadyAsync(room.Code, "mallory", true)).ErrorCode.Should().Be(ErrorCodes.NotInRoom);
    }

    [Fact]
    public async Task HostCanStart_OnlyWhenEnoughPlayersJoined()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        (await fx.Manager.StartGameAsync(room.Code, "alice")).ErrorCode.Should().Be(ErrorCodes.NotEnoughPlayers);
        await fx.JoinAsync(room.Code);
        (await fx.Manager.StartGameAsync(room.Code, "bob")).ErrorCode.Should().Be(ErrorCodes.NotHost);
        var started = await fx.Manager.StartGameAsync(room.Code, "alice");
        started.Value!.GameStarted.Should().BeTrue();
        (await fx.Manager.StartGameAsync(room.Code, "alice")).ErrorCode.Should().Be(ErrorCodes.GameInProgress);
    }

    // ── Moves and concurrency ─────────────────────────────────

    [Fact]
    public async Task MakeMove_AppliesTheMove_AndBumpsTheVersion()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        var result = await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(2), 1);
        result.Success.Should().BeTrue(result.ErrorCode);
        var session = result.Value!.Snapshot.Session!;
        session.StateVersion.Should().Be(2);
        session.MoveCount.Should().Be(1);
        session.CurrentTurnPlayerId.Should().Be("bob");
        ((NimState)session.State).Stones.Should().Be(NimEngine.StartStones - 2);
    }

    [Fact]
    public async Task MakeMove_RejectsWrongTurn_StaleVersion_AndIllegalMoves_WithoutChangingState()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        (await fx.Manager.MakeMoveAsync(code, "bob", RoomFixture.Move(1), 1)).ErrorCode.Should().Be(ErrorCodes.NotYourTurn);
        (await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), 7)).ErrorCode.Should().Be(ErrorCodes.StaleState);
        (await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(9), 1)).ErrorCode.Should().Be(ErrorCodes.IllegalMove);
        (await fx.Manager.MakeMoveAsync(code, "alice", "garbage", 1)).ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        (await fx.Manager.MakeMoveAsync(code, "alice", "", 1)).ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        (await fx.Manager.MakeMoveAsync(code, "eve", RoomFixture.Move(1), 1)).ErrorCode.Should().Be(ErrorCodes.NotInRoom);

        var session = (await fx.SnapshotAsync(code)).Session!;
        session.StateVersion.Should().Be(1);
        ((NimState)session.State).Stones.Should().Be(NimEngine.StartStones);
    }

    [Fact]
    public async Task HundredParallelMoves_OnTheSameTurn_YieldExactlyOneAcceptedMove()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(
            () => fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), 1))).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Success).Should().Be(1);
        results.Where(r => !r.Success).Select(r => r.ErrorCode).Should()
            .OnlyContain(c => c == ErrorCodes.NotYourTurn || c == ErrorCodes.StaleState);
        var session = (await fx.SnapshotAsync(code)).Session!;
        session.StateVersion.Should().Be(2);
        session.MoveCount.Should().Be(1);
        ((NimState)session.State).Stones.Should().Be(NimEngine.StartStones - 1);
    }

    [Fact]
    public async Task ParallelMovesFromBothPlayers_StayConsistent()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();

        // Each player hammers the room; only moves made on the right turn with the right version may land.
        async Task<int> Hammer(string player)
        {
            var accepted = 0;
            for (var i = 0; i < 200; i++)
            {
                var version = (await fx.SnapshotAsync(code)).Session!.StateVersion;
                var result = await fx.Manager.MakeMoveAsync(code, player, RoomFixture.Move(1), version);
                if (result.Success)
                {
                    accepted++;
                }
            }

            return accepted;
        }

        var counts = await Task.WhenAll(Task.Run(() => Hammer("alice")), Task.Run(() => Hammer("bob")));
        var final = await fx.SnapshotAsync(code);
        var session = final.Session!;
        counts.Sum().Should().Be(session.MoveCount);
        session.StateVersion.Should().Be(session.MoveCount + 1);
        ((NimState)session.State).Stones.Should().Be(NimEngine.StartStones - session.MoveCount);
        Math.Abs(counts[0] - counts[1]).Should().BeLessThanOrEqualTo(1, "turns strictly alternate");
    }

    [Fact]
    public async Task FinishingMove_EndsTheGame_AndReportsTheResultOnce()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        var stones = NimEngine.StartStones;
        var version = 1;
        var players = new[] { "alice", "bob" };
        RoomUpdate? last = null;
        for (var turn = 0; stones > 0; turn++)
        {
            var take = Math.Min(3, stones);
            var result = await fx.Manager.MakeMoveAsync(code, players[turn % 2], RoomFixture.Move(take), version++);
            result.Success.Should().BeTrue(result.ErrorCode);
            last = result.Value;
            stones -= take;
        }

        last!.NewResult.Should().NotBeNull();
        last.Snapshot.Status.Should().Be(RoomStatus.Finished);
        last.Snapshot.Session!.Result!.WinnerPlayerId.Should().Be(players[(NimEngine.StartStones + 2) / 3 % 2 == 0 ? 1 : 0]);
        (await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), version)).ErrorCode.Should().Be(ErrorCodes.GameOver);
    }

    // ── Resign, draw, rematch ─────────────────────────────────

    [Fact]
    public async Task Resign_EndsTheGame_ForTheOpponent()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        var result = await fx.Manager.ResignAsync(code, "bob");
        result.Value!.NewResult.Should().Be(GameResult.Win("alice", GameEndReason.Resignation));
        result.Value.Snapshot.Status.Should().Be(RoomStatus.Finished);
        (await fx.Manager.ResignAsync(code, "bob")).ErrorCode.Should().Be(ErrorCodes.GameOver);
    }

    [Fact]
    public async Task DrawOffer_CanBeAccepted_Declined_OrAcceptedByOfferingBack()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();

        (await fx.Manager.RespondDrawAsync(code, "bob", true)).ErrorCode.Should().Be(ErrorCodes.NoDrawOffer);
        var offered = await fx.Manager.OfferDrawAsync(code, "alice");
        offered.Value!.Snapshot.Session!.DrawOfferedBy.Should().Be("alice");
        (await fx.Manager.OfferDrawAsync(code, "alice")).ErrorCode.Should().Be(ErrorCodes.DrawAlreadyOffered);
        (await fx.Manager.RespondDrawAsync(code, "alice", true)).ErrorCode.Should().Be(ErrorCodes.NoDrawOffer, "you cannot answer your own offer");

        var declined = await fx.Manager.RespondDrawAsync(code, "bob", false);
        declined.Value!.Snapshot.Session!.DrawOfferedBy.Should().BeNull();
        declined.Value.NewResult.Should().BeNull();

        await fx.Manager.OfferDrawAsync(code, "alice");
        var accepted = await fx.Manager.RespondDrawAsync(code, "bob", true);
        accepted.Value!.NewResult.Should().Be(GameResult.Draw(GameEndReason.DrawAgreement));
    }

    [Fact]
    public async Task OfferingBack_WhenTheOpponentAlreadyOffered_AcceptsTheDraw()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        await fx.Manager.OfferDrawAsync(code, "alice");
        var result = await fx.Manager.OfferDrawAsync(code, "bob");
        result.Value!.NewResult.Should().Be(GameResult.Draw(GameEndReason.DrawAgreement));
    }

    [Fact]
    public async Task AMove_DeclinesAPendingDrawOffer()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        await fx.Manager.OfferDrawAsync(code, "bob");
        var moved = await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), 1);
        moved.Value!.Snapshot.Session!.DrawOfferedBy.Should().BeNull();
    }

    [Fact]
    public async Task Rematch_NeedsBothPlayers_AndSwapsColors()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        (await fx.Manager.RequestRematchAsync(code, "alice")).ErrorCode.Should().Be(ErrorCodes.RematchNotAvailable);

        await fx.Manager.ResignAsync(code, "bob");
        var first = await fx.Manager.RequestRematchAsync(code, "alice");
        first.Value!.GameStarted.Should().BeFalse();
        first.Value.Snapshot.RematchVotes.Should().Equal("alice");
        (await fx.Manager.RequestRematchAsync(code, "alice")).Value!.GameStarted.Should().BeFalse("votes are idempotent");

        var second = await fx.Manager.RequestRematchAsync(code, "bob");
        second.Value!.GameStarted.Should().BeTrue();
        var snapshot = second.Value.Snapshot;
        snapshot.Status.Should().Be(RoomStatus.InProgress);
        snapshot.Session!.PlayerIdsByColor.Should().Equal("bob", "alice");
        snapshot.Session.CurrentTurnPlayerId.Should().Be("bob");
        snapshot.Session.StateVersion.Should().Be(1);
        snapshot.RematchVotes.Should().BeEmpty();
        snapshot.GamesPlayed.Should().Be(1);

        await fx.Manager.ResignAsync(code, "alice");
        await fx.Manager.RequestRematchAsync(code, "alice");
        var third = await fx.Manager.RequestRematchAsync(code, "bob");
        third.Value!.Snapshot.Session!.PlayerIdsByColor.Should().Equal("alice", "bob");
    }

    // ── Chat ──────────────────────────────────────────────────

    [Fact]
    public async Task Chat_TrimsStripsControlCharacters_AndEnforcesTheLimit()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        var ok = await fx.Manager.SendChatAsync(code, "alice", "  hello\u0007 <b>there</b>\n ");
        ok.Value!.Text.Should().Be("hello <b>there</b>", "markup is kept as text; clients never render HTML");
        ok.Value.DisplayName.Should().Be("ALICE");

        (await fx.Manager.SendChatAsync(code, "alice", new string('x', 200))).Success.Should().BeTrue();
        (await fx.Manager.SendChatAsync(code, "alice", new string('x', 201))).ErrorCode.Should().Be(ErrorCodes.MessageInvalid);
        (await fx.Manager.SendChatAsync(code, "alice", "   ")).ErrorCode.Should().Be(ErrorCodes.MessageInvalid);
        (await fx.Manager.SendChatAsync(code, "mallory", "hi")).ErrorCode.Should().Be(ErrorCodes.NotInRoom);
    }

    // ── Leaving ───────────────────────────────────────────────

    [Fact]
    public async Task HostLeavingAWaitingRoom_PassesTheHostToTheNextPlayer_AndTheLastLeaverClosesIt()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        await fx.JoinAsync(room.Code);

        var left = await fx.Manager.LeaveRoomAsync(room.Code, "alice");
        left.Value!.Snapshot!.HostPlayerId.Should().Be("bob");
        left.Value.Snapshot.Players.Should().ContainSingle();
        fx.Manager.GetRoomCodeOfPlayer("alice").Should().BeNull();

        var closed = await fx.Manager.LeaveRoomAsync(room.Code, "bob");
        closed.Value!.Snapshot.Should().BeNull();
        fx.Manager.RoomCount.Should().Be(0);
        (await fx.Manager.GetRoomAsync(room.Code)).ErrorCode.Should().Be(ErrorCodes.RoomNotFound);
    }

    [Fact]
    public async Task LeavingARunningGame_CountsAsResigning()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        var left = await fx.Manager.LeaveRoomAsync(code, "alice");
        left.Value!.NewResult.Should().Be(GameResult.Win("bob", GameEndReason.Resignation));
        left.Value.Snapshot!.Status.Should().Be(RoomStatus.Finished);
        (await fx.Manager.RequestRematchAsync(code, "bob")).ErrorCode.Should().Be(ErrorCodes.RematchNotAvailable);
    }

    // ── Clocks ────────────────────────────────────────────────

    private static readonly TimeControl FiveWithIncrement = new(5, 3);

    [Fact]
    public async Task Clock_RunsForTheMover_AndTheIncrementIsAddedAfterTheirMove()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync(FiveWithIncrement);
        var start = (await fx.SnapshotAsync(code)).Session!.Clock!;
        start.RemainingMs["alice"].Should().Be(300_000);
        start.RunningPlayerId.Should().Be("alice");

        fx.Time.Advance(TimeSpan.FromSeconds(10));
        var running = (await fx.SnapshotAsync(code)).Session!.Clock!;
        running.RemainingMs["alice"].Should().Be(290_000);
        running.RemainingMs["bob"].Should().Be(300_000);

        var moved = await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), 1);
        var after = moved.Value!.Snapshot.Session!.Clock!;
        after.RemainingMs["alice"].Should().Be(293_000, "10 s spent, 3 s increment");
        after.RunningPlayerId.Should().Be("bob");

        fx.Time.Advance(TimeSpan.FromSeconds(4));
        (await fx.SnapshotAsync(code)).Session!.Clock!.RemainingMs["bob"].Should().Be(296_000);
        (await fx.SnapshotAsync(code)).Session!.Clock!.RemainingMs["alice"].Should().Be(293_000, "idle clocks do not tick");
    }

    [Fact]
    public async Task TimerSweep_DeclaresATimeout_WhenTheMoversClockRunsOut()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync(new TimeControl(5, 0));
        fx.Time.Advance(TimeSpan.FromMinutes(4));
        (await fx.Manager.ProcessTimersAsync()).Ended.Should().BeEmpty();

        fx.Time.Advance(TimeSpan.FromMinutes(1));
        var report = await fx.Manager.ProcessTimersAsync();
        var ended = report.Ended.Should().ContainSingle().Subject;
        ended.NewResult.Should().Be(GameResult.Win("bob", GameEndReason.Timeout));
        ended.Snapshot.Session!.Clock!.RemainingMs["alice"].Should().Be(0);
        ended.Snapshot.Status.Should().Be(RoomStatus.Finished);
        (await fx.Manager.ProcessTimersAsync()).Ended.Should().BeEmpty("the result is reported once");
    }

    [Fact]
    public async Task AMoveMadeAfterTheFlagFell_LosesOnTime_AndIsNotApplied()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync(new TimeControl(5, 0));
        fx.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var result = await fx.Manager.MakeMoveAsync(code, "alice", RoomFixture.Move(1), 1);
        result.Success.Should().BeTrue();
        result.Value!.NewResult.Should().Be(GameResult.Win("bob", GameEndReason.Timeout));
        ((NimState)result.Value.Snapshot.Session!.State).Stones.Should().Be(NimEngine.StartStones);
    }

    [Fact]
    public async Task UntimedGames_NeverTimeOut()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        fx.Time.Advance(TimeSpan.FromMinutes(25));
        (await fx.Manager.ProcessTimersAsync()).Ended.Should().BeEmpty();
        (await fx.SnapshotAsync(code)).Session!.Clock.Should().BeNull();
    }

    // ── Disconnects ───────────────────────────────────────────

    [Fact]
    public async Task Disconnect_StartsAGraceCountdown_AndReconnectingCancelsIt()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();

        var info = await fx.Manager.HandleDisconnectAsync("c-bob");
        info.Value!.PlayerId.Should().Be("bob");
        info.Value.GraceSeconds.Should().Be(60);
        info.Value.Snapshot.Players.Single(p => p.Player.PlayerId == "bob").IsConnected.Should().BeFalse();
        fx.Manager.GetConnectionId("bob").Should().BeNull();

        fx.Time.Advance(TimeSpan.FromSeconds(20));
        var countdown = (await fx.Manager.ProcessTimersAsync()).Countdowns.Should().ContainSingle().Subject;
        countdown.PlayerId.Should().Be("bob");
        countdown.SecondsLeft.Should().Be(40);

        var back = await fx.Manager.RejoinRoomAsync("c-bob-2", code, "bob");
        back.Success.Should().BeTrue();
        back.Value!.Snapshot.Players.Should().OnlyContain(p => p.IsConnected);
        fx.Manager.GetConnectionId("bob").Should().Be("c-bob-2");

        fx.Time.Advance(TimeSpan.FromMinutes(5));
        var report = await fx.Manager.ProcessTimersAsync();
        report.Ended.Should().BeEmpty();
        report.Countdowns.Should().BeEmpty();
    }

    [Fact]
    public async Task Disconnect_PastTheGracePeriod_ForfeitsTheGame()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        await fx.Manager.HandleDisconnectAsync("c-bob");

        fx.Time.Advance(TimeSpan.FromSeconds(59));
        (await fx.Manager.ProcessTimersAsync()).Ended.Should().BeEmpty();

        fx.Time.Advance(TimeSpan.FromSeconds(2));
        var ended = (await fx.Manager.ProcessTimersAsync()).Ended.Should().ContainSingle().Subject;
        ended.NewResult.Should().Be(GameResult.Win("alice", GameEndReason.Abandonment));
        (await fx.SnapshotAsync(code)).Status.Should().Be(RoomStatus.Finished);
    }

    [Fact]
    public async Task Disconnect_GracePeriodIsConfigurable()
    {
        var fx = new RoomFixture(o => o.DisconnectGraceSeconds = 5);
        await fx.StartedRoomAsync();
        await fx.Manager.HandleDisconnectAsync("c-alice");
        fx.Time.Advance(TimeSpan.FromSeconds(6));
        (await fx.Manager.ProcessTimersAsync()).Ended.Should().ContainSingle()
            .Which.NewResult.Should().Be(GameResult.Win("bob", GameEndReason.Abandonment));
    }

    [Fact]
    public async Task StaleConnectionDrop_AfterAReconnect_IsIgnored()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        await fx.Manager.RejoinRoomAsync("c-bob-new", code, "bob");
        var late = await fx.Manager.HandleDisconnectAsync("c-bob");
        late.Success.Should().BeFalse("the old socket closing must not mark the player as gone");
        (await fx.SnapshotAsync(code)).Players.Should().OnlyContain(p => p.IsConnected);
    }

    [Fact]
    public async Task Rejoin_RequiresMembership()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        (await fx.Manager.RejoinRoomAsync("c-x", code, "mallory")).ErrorCode.Should().Be(ErrorCodes.NotInRoom);
        (await fx.Manager.RejoinRoomAsync("c-x", "ZZZZZZ", "alice")).ErrorCode.Should().Be(ErrorCodes.RoomNotFound);
    }

    [Fact]
    public async Task DisconnectedPlayerInALobby_IsRemovedAfterTheGracePeriod()
    {
        var fx = new RoomFixture();
        var room = await fx.CreateAsync();
        await fx.JoinAsync(room.Code);
        await fx.Manager.HandleDisconnectAsync("c-bob");

        fx.Time.Advance(TimeSpan.FromSeconds(61));
        var departure = (await fx.Manager.ProcessTimersAsync()).Departures.Should().ContainSingle().Subject;
        departure.PlayerId.Should().Be("bob");
        departure.Snapshot!.Players.Should().ContainSingle().Which.Player.PlayerId.Should().Be("alice");
    }

    // ── Cleanup ───────────────────────────────────────────────

    [Fact]
    public async Task Cleanup_RemovesIdleRooms_ButKeepsActiveOnes()
    {
        var fx = new RoomFixture();
        var idle = await fx.CreateAsync("p1", "c1");
        var active = await fx.CreateAsync("p2", "c2");

        fx.Time.Advance(TimeSpan.FromMinutes(20));
        await fx.Manager.SendChatAsync(active.Code, "p2", "still here");
        fx.Time.Advance(TimeSpan.FromMinutes(15));

        var removed = await fx.Manager.CleanupAsync();
        removed.Should().Equal(idle.Code);
        (await fx.Manager.GetRoomAsync(idle.Code)).ErrorCode.Should().Be(ErrorCodes.RoomNotFound);
        (await fx.Manager.GetRoomAsync(active.Code)).Success.Should().BeTrue();
        fx.Manager.GetRoomCodeOfPlayer("p1").Should().BeNull("player bookkeeping is released with the room");
        fx.Manager.GetConnectionId("p1").Should().BeNull();
    }

    [Fact]
    public async Task Cleanup_RemovesFinishedRooms_SoonerThanIdleOnes()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        await fx.Manager.ResignAsync(code, "alice");

        fx.Time.Advance(TimeSpan.FromMinutes(9));
        (await fx.Manager.CleanupAsync()).Should().BeEmpty();
        fx.Time.Advance(TimeSpan.FromMinutes(2));
        (await fx.Manager.CleanupAsync()).Should().Equal(code);
        fx.Manager.RoomCount.Should().Be(0);
    }

    [Fact]
    public async Task OperationsOnARemovedRoom_FailCleanly_EvenWhenRacingTheCleanup()
    {
        var fx = new RoomFixture();
        var code = await fx.StartedRoomAsync();
        fx.Time.Advance(TimeSpan.FromHours(2));

        var tasks = new List<Task<OperationResult<RoomUpdate>>>();
        for (var i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => fx.Manager.MakeMoveAsync(code, "bob", RoomFixture.Move(1), 1)));
        }

        // Rejected moves do not count as activity, so the sweep may remove the room while they are in flight.
        var cleanup = Task.Run(() => fx.Manager.CleanupAsync());
        var results = await Task.WhenAll(tasks);
        await cleanup;

        results.Where(r => !r.Success).Select(r => r.ErrorCode).Should()
            .OnlyContain(c => c == ErrorCodes.RoomNotFound || c == ErrorCodes.NotYourTurn || c == ErrorCodes.StaleState);
        fx.Manager.RoomCount.Should().Be(0);
    }
}
