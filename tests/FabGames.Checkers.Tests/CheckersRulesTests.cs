using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using FabGames.Games.Checkers;
using FabGames.Games.Checkers.Models;

namespace FabGames.Checkers.Tests;

public class CheckersRulesTests
{
    // Index 0 plays the light side, index 1 the dark side (who moves first).
    private const string Light = "light";
    private const string Dark = "dark";
    private static readonly CheckersEngine Engine = new();

    /// <summary>
    /// Builds a position from placements such as <c>"bd4"</c>: b/B = dark man/king, w/W = light man/king,
    /// followed by the square.
    /// </summary>
    private static CheckersGameState Position(PlayerColor toMove, params string[] placements) =>
        Engine.NewGameFromBoard(BoardOf(placements), toMove, [Light, Dark]);

    private static CheckersBoard BoardOf(string[] placements)
    {
        var grid = new char[8, 8];
        for (var r = 0; r < 8; r++)
        {
            for (var f = 0; f < 8; f++)
            {
                grid[r, f] = '.';
            }
        }

        foreach (var placement in placements)
        {
            CheckersPosition.TryParse(placement[1..], out var square).Should().BeTrue(placement);
            grid[7 - square.Rank, square.File] = placement[0];
        }

        var rows = Enumerable.Range(0, 8).Select(r => new string(Enumerable.Range(0, 8).Select(f => grid[r, f]).ToArray()));
        return CheckersBoard.Parse(string.Join('\n', rows));
    }

    private static string PlayerOf(CheckersGameState s) => s.ActiveColor == PlayerColor.White ? Light : Dark;

    private static CheckersGameState Play(CheckersGameState state, string from, params string[] path)
    {
        var result = Engine.Apply(state, PlayerOf(state), new CheckersMoveAction(from, path));
        result.Success.Should().BeTrue($"{from}->{string.Join(">", path)} should be legal (got {result.ErrorCode})");
        return result.NewState!;
    }

    private static string Reject(CheckersGameState state, string from, params string[] path)
    {
        var result = Engine.Apply(state, PlayerOf(state), new CheckersMoveAction(from, path));
        result.Success.Should().BeFalse($"{from}->{string.Join(">", path)} should be rejected");
        return result.ErrorCode!;
    }

    private static string At(CheckersGameState s, string square)
    {
        CheckersPosition.TryParse(square, out var p);
        var piece = s.Board[p.Index];
        return piece.IsEmpty ? "." : (piece.IsKing ? "K" : "m") + (piece.Color == PlayerColor.White ? "L" : "D");
    }

    private static IEnumerable<string> Notations(CheckersGameState s) => s.LegalMoves.Select(m => m.ToNotation());

    // ── Setup ─────────────────────────────────────────────────

    [Fact]
    public void StartingPosition_Has12MenPerSide_OnDarkSquares_AndDarkMovesFirst()
    {
        var state = Engine.NewGame([Light, Dark]);
        state.Board.Count(PlayerColor.White).Should().Be(12);
        state.Board.Count(PlayerColor.Black).Should().Be(12);
        state.ActiveColor.Should().Be(CheckersEngine.FirstMover).And.Be(PlayerColor.Black);
        Engine.GetCurrentTurnPlayerId(state).Should().Be(Dark);
        At(state, "a1").Should().Be("mL");
        At(state, "b8").Should().Be("mD");
        At(state, "b1").Should().Be(".", "light squares stay empty");
        state.LegalMoves.Should().HaveCount(7, "dark has 7 opening moves");
    }

    [Fact]
    public void LightPlayer_CannotOpen()
    {
        var state = Engine.NewGame([Light, Dark]);
        Engine.Apply(state, Light, new CheckersMoveAction("c3", ["d4"])).ErrorCode.Should().Be(ErrorCodes.NotYourTurn);
    }

    // ── Simple moves ──────────────────────────────────────────

    [Fact]
    public void Men_MoveOneSquareForwardOnly()
    {
        var state = Position(PlayerColor.Black, "bd4");
        Notations(state).Should().BeEquivalentTo("d4-c3", "d4-e3");
        Reject(state, "d4", "c5").Should().Be(ErrorCodes.IllegalMove, "men never move backward");
        Reject(state, "d4", "e5").Should().Be(ErrorCodes.IllegalMove);
        Reject(state, "d4", "d3").Should().Be(ErrorCodes.IllegalMove, "no orthogonal moves");
        Reject(state, "d4", "b2").Should().Be(ErrorCodes.IllegalMove, "two squares only with a capture");
    }

    [Fact]
    public void Kings_MoveOneSquareInAnyDirection_NoFlyingKings()
    {
        var state = Position(PlayerColor.Black, "Bd4");
        Notations(state).Should().BeEquivalentTo("d4-c3", "d4-e3", "d4-c5", "d4-e5");
        Reject(state, "d4", "f6").Should().Be(ErrorCodes.IllegalMove, "a king moves a single square");
    }

    [Fact]
    public void CannotMoveOntoOccupiedSquare_FromEmptySquare_OrWithTheOpponentsPiece()
    {
        var state = Engine.NewGame([Light, Dark]);
        Reject(state, "b8", "a7").Should().Be(ErrorCodes.IllegalMove, "a7 is occupied");
        Reject(state, "e4", "d3").Should().Be(ErrorCodes.IllegalMove, "nothing on e4");
        Reject(state, "a1", "b2").Should().Be(ErrorCodes.IllegalMove, "not your piece");
    }

    [Fact]
    public void Rejects_MalformedActions()
    {
        var state = Engine.NewGame([Light, Dark]);
        Reject(state, "z9", "a1").Should().Be(ErrorCodes.InvalidAction);
        Reject(state, "b6", "zz").Should().Be(ErrorCodes.InvalidAction);
        Reject(state, "b6").Should().Be(ErrorCodes.InvalidAction, "an empty path is meaningless");
        IGameEngine engine = Engine;
        engine.ApplyAction(state, Dark, "{").ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        engine.ApplyAction(state, Dark, "null").ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        engine.ApplyAction(state, Dark, """{"from":"b6","path":["a5"]}""").Success.Should().BeTrue();
    }

    // ── Mandatory capture ─────────────────────────────────────

    [Fact]
    public void Capture_IsMandatory_AndSimpleMovesAreRefused()
    {
        var state = Position(PlayerColor.Black, "bd4", "wc3");
        state.MustCapture.Should().BeTrue();
        Notations(state).Should().ContainSingle().Which.Should().Be("d4xb2");
        Reject(state, "d4", "e3").Should().Be(ErrorCodes.MandatoryCapture);

        var after = Play(state, "d4", "b2");
        At(after, "c3").Should().Be(".", "the jumped piece is removed");
        At(after, "b2").Should().Be("mD");
        At(after, "d4").Should().Be(".");
    }

    [Fact]
    public void Capture_IsMandatory_EvenForAnotherPiece()
    {
        // The capture belongs to d4, but dark's other man may not make a quiet move instead.
        var state = Position(PlayerColor.Black, "bd4", "wc3", "bg7");
        Reject(state, "g7", "f6").Should().Be(ErrorCodes.MandatoryCapture);
    }

    [Fact]
    public void PlayerMayChoose_AmongDifferentCaptures()
    {
        var state = Position(PlayerColor.Black, "bd4", "wc3", "we3");
        Notations(state).Should().BeEquivalentTo("d4xb2", "d4xf2");
        Play(state, "d4", "f2").Board.Count(PlayerColor.White).Should().Be(1);
    }

    [Fact]
    public void Men_CannotCaptureBackward()
    {
        var state = Position(PlayerColor.Black, "bd4", "wc5");
        state.MustCapture.Should().BeFalse();
        Notations(state).Should().BeEquivalentTo("d4-c3", "d4-e3");
    }

    [Fact]
    public void Kings_CaptureBackward()
    {
        var state = Position(PlayerColor.Black, "Bd4", "wc5");
        Notations(state).Should().ContainSingle().Which.Should().Be("d4xb6");
    }

    [Fact]
    public void Cannot_JumpOntoAnOccupiedSquare()
    {
        var state = Position(PlayerColor.White, "wb2", "bc3", "bd4");
        state.MustCapture.Should().BeFalse("d4 is occupied, so c3 cannot be jumped");
        Notations(state).Should().ContainSingle().Which.Should().Be("b2-a3");
    }

    // ── Multi-jumps ───────────────────────────────────────────

    [Fact]
    public void MultiJump_MustBeCompleted_AndIsAtomic()
    {
        var state = Position(PlayerColor.Black, "bg7", "wf6", "wd4");
        Notations(state).Should().ContainSingle().Which.Should().Be("g7xe5xc3");
        Reject(state, "g7", "e5").Should().Be(ErrorCodes.IncompleteCapture, "the sequence must be continued");

        var done = Play(state, "g7", "e5", "c3");
        At(done, "f6").Should().Be(".");
        At(done, "d4").Should().Be(".");
        At(done, "c3").Should().Be("mD");
        done.Result.Should().Be(GameResult.Win(Dark, GameEndReason.NoPieces));
    }

    [Fact]
    public void MultiJump_TurnStaysWithThePlayer_UntilTheSequenceEnds()
    {
        // After the full jump it is the opponent's move; the whole path was one action.
        var state = Position(PlayerColor.Black, "bg7", "wf6", "wd4", "wa1");
        var done = Play(state, "g7", "e5", "c3");
        done.ActiveColor.Should().Be(PlayerColor.White);
        done.Moves.Should().ContainSingle().Which.Notation.Should().Be("g7xe5xc3");
    }

    [Fact]
    public void MultiJump_CanBranch()
    {
        var state = Position(PlayerColor.Black, "bg7", "wf6", "wd4", "wf4");
        Notations(state).Should().BeEquivalentTo("g7xe5xc3", "g7xe5xg3");
    }

    [Fact]
    public void FreeChoice_NoMaximumCaptureRule()
    {
        var state = Position(PlayerColor.Black, "bg7", "wf6", "wd4", "bc5", "wb4");
        Notations(state).Should().BeEquivalentTo("g7xe5xc3", "c5xa3", "c5xe3");
        Play(state, "c5", "a3").Moves.Should().ContainSingle();
    }

    [Fact]
    public void King_CannotJumpTheSamePieceTwice()
    {
        var state = Position(
            PlayerColor.Black, "Ba1", "wb2", "wb4", "wb6", "wd6", "wd4");
        var moves = state.LegalMoves;
        moves.Select(m => m.ToNotation()).Should()
            .BeEquivalentTo("a1xc3xa5xc7xe5xc3", "a1xc3xe5xc7xa5xc3");
        moves.Should().OnlyContain(m => m.Captured.Distinct().Count() == 5 && m.Captured.Length == 5);
    }

    // ── Promotion ─────────────────────────────────────────────

    [Fact]
    public void ReachingTheFarRow_CrownsTheMan()
    {
        var light = Play(Position(PlayerColor.White, "wc7", "bg3"), "c7", "b8");
        At(light, "b8").Should().Be("KL");
        light.LastMove!.Move.Promotes.Should().BeTrue();

        var dark = Play(Position(PlayerColor.Black, "bd2", "wg7"), "d2", "c1");
        At(dark, "c1").Should().Be("KD");
    }

    [Fact]
    public void Promotion_EndsTheTurn_EvenWhenAKingCouldKeepJumping()
    {
        // f6 x e7 lands on d8 and is crowned. A king on d8 could continue over c7 to b6, but the turn ends.
        var state = Position(PlayerColor.White, "wf6", "be7", "bc7");
        var move = state.LegalMoves.Should().ContainSingle().Subject;
        move.ToNotation().Should().Be("f6xd8");
        move.Promotes.Should().BeTrue();

        var after = Play(state, "f6", "d8");
        At(after, "d8").Should().Be("KL");
        At(after, "c7").Should().Be("mD", "c7 was not captured: crowning ended the turn");
        after.ActiveColor.Should().Be(PlayerColor.Black);
        after.Result.Should().BeNull();
        Reject(state, "f6", "d8", "b6").Should().Be(ErrorCodes.MandatoryCapture, "continuing past the crowning square is not a legal capture path");
    }

    [Fact]
    public void King_MovesBackwards_ButOnlyOneSquare()
    {
        var state = Position(PlayerColor.White, "We5");
        Notations(state).Should().BeEquivalentTo("e5-d6", "e5-f6", "e5-d4", "e5-f4");
    }

    // ── End of game ───────────────────────────────────────────

    [Fact]
    public void NoLegalMoves_IsALossForTheBlockedPlayer()
    {
        // The dark man on b2 is hemmed in by light men on a1 and c1; jumping them would leave the board.
        var state = Position(PlayerColor.Black, "bb2", "wa1", "wc1");
        state.LegalMoves.Should().BeEmpty();
        state.Result.Should().Be(GameResult.Win(Light, GameEndReason.NoLegalMoves));
    }

    [Fact]
    public void CapturingTheLastPiece_WinsImmediately()
    {
        var after = Play(Position(PlayerColor.Black, "bd4", "wc3"), "d4", "b2");
        after.Result.Should().Be(GameResult.Win(Dark, GameEndReason.NoPieces));
        Engine.Apply(after, Light, new CheckersMoveAction("a1", ["b2"])).ErrorCode.Should().Be(ErrorCodes.GameOver);
    }

    [Fact]
    public void FortyMovesPerSideWithoutProgress_IsADraw()
    {
        var engine = new CheckersEngine(drawMoveLimitPerSide: 40);
        var state = engine.NewGameFromBoard(BoardOf(["Ba5", "Wh2"]), PlayerColor.Black, [Light, Dark]);
        string[][] cycle = [["a5", "b6"], ["h2", "g1"], ["b6", "a5"], ["g1", "h2"]];

        for (var ply = 0; ply < 80; ply++)
        {
            state.Result.Should().BeNull($"ply {ply} is still below the limit");
            var step = cycle[ply % 4];
            var player = state.ActiveColor == PlayerColor.White ? Light : Dark;
            var next = engine.Apply(state, player, new CheckersMoveAction(step[0], [step[1]]));
            next.Success.Should().BeTrue($"ply {ply}: {step[0]}-{step[1]} ({next.ErrorCode})");
            state = next.NewState!;
        }

        state.Result.Should().Be(GameResult.Draw(GameEndReason.FortyMoveRule));
        state.PliesSinceProgress.Should().Be(80);
    }

    [Fact]
    public void DrawLimit_IsConfigurable()
    {
        var engine = new CheckersEngine(drawMoveLimitPerSide: 2);
        var state = engine.NewGameFromBoard(BoardOf(["Ba5", "Wh2"]), PlayerColor.Black, [Light, Dark]);
        string[][] cycle = [["a5", "b6"], ["h2", "g1"], ["b6", "a5"], ["g1", "h2"]];
        for (var ply = 0; ply < 4; ply++)
        {
            var player = state.ActiveColor == PlayerColor.White ? Light : Dark;
            state = engine.Apply(state, player, new CheckersMoveAction(cycle[ply][0], [cycle[ply][1]])).NewState!;
        }

        state.Result.Should().Be(GameResult.Draw(GameEndReason.FortyMoveRule));
    }

    [Fact]
    public void ManMoves_ResetTheDrawCounter_KingMovesIncrementIt()
    {
        var state = Position(PlayerColor.Black, "Bd6", "bg7", "Wa1");
        state.PliesSinceProgress.Should().Be(0);
        var afterKing = Play(state, "d6", "e5");
        afterKing.PliesSinceProgress.Should().Be(1);
        var afterLightKing = Play(afterKing, "a1", "b2");
        afterLightKing.PliesSinceProgress.Should().Be(2);
        var afterMan = Play(afterLightKing, "g7", "f6");
        afterMan.PliesSinceProgress.Should().Be(0);
    }

    [Fact]
    public void Resign_AndTimeout_GiveTheOpponentTheWin()
    {
        var state = Engine.NewGame([Light, Dark]);
        ((IGameEngine)Engine).Resign(state, Dark).Should().Be(GameResult.Win(Light, GameEndReason.Resignation));
        Engine.DeclareTimeout(state, Light).Should().Be(GameResult.Win(Dark, GameEndReason.Timeout));
    }

    [Fact]
    public void LegalActions_MatchTheEngine_AndAreEmptyOffTurn()
    {
        var state = Engine.NewGame([Light, Dark]);
        Engine.GetLegalActions(state, Light).Should().BeEmpty();
        var actions = Engine.GetLegalActions(state, Dark);
        actions.Should().HaveCount(7);
        foreach (var action in actions)
        {
            Engine.Apply(state, Dark, action).Success.Should().BeTrue();
        }
    }

    [Fact]
    public void ShortGame_WithForcedCaptures_PlaysThroughTheEngine()
    {
        var state = Engine.NewGame([Light, Dark]);
        state = Play(state, "f6", "e5");
        state = Play(state, "c3", "d4");

        state.MustCapture.Should().BeTrue("dark's e5 man must take d4");
        Notations(state).Should().ContainSingle().Which.Should().Be("e5xc3");
        Reject(state, "e5", "f4").Should().Be(ErrorCodes.MandatoryCapture);

        state = Play(state, "e5", "c3");
        state.MustCapture.Should().BeTrue("light must retake");
        Notations(state).Should().BeEquivalentTo("b2xd4", "d2xb4");

        state = Play(state, "b2", "d4");
        state.Board.Count(PlayerColor.White).Should().Be(11);
        state.Board.Count(PlayerColor.Black).Should().Be(11);
        state.Moves.Select(m => m.Notation).Should().Equal("f6-e5", "c3-d4", "e5xc3", "b2xd4");
    }
}
