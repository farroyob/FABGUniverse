using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;
using FabGames.Games.Chess;
using FabGames.Games.Chess.Logic;
using FabGames.Games.Chess.Models;

namespace FabGames.Chess.Tests;

public class ChessRulesTests
{
    private const string W = "white";
    private const string B = "black";
    private static readonly ChessEngine Engine = new();

    private static ChessGameState Start(string? fen = null) =>
        fen is null ? Engine.NewGame([W, B]) : Engine.NewGameFromFen(fen, [W, B]);

    private static ChessGameState Play(ChessGameState state, string from, string to, string? promotion = null)
    {
        var player = state.Board.ActiveColor == PlayerColor.White ? W : B;
        var result = Engine.Apply(state, player, new ChessMoveAction(from, to, promotion));
        result.Success.Should().BeTrue($"{from}{to} should be legal (got {result.ErrorCode})");
        return result.NewState!;
    }

    private static string Reject(ChessGameState state, string from, string to, string? promotion = null)
    {
        var player = state.Board.ActiveColor == PlayerColor.White ? W : B;
        var result = Engine.Apply(state, player, new ChessMoveAction(from, to, promotion));
        result.Success.Should().BeFalse($"{from}{to} should be illegal");
        return result.ErrorCode!;
    }

    private static ChessGameState Sequence(ChessGameState state, params string[] moves)
    {
        foreach (var m in moves)
        {
            state = Play(state, m[..2], m[2..4], m.Length > 4 ? m[4..] : null);
        }

        return state;
    }

    private static IEnumerable<string> Destinations(ChessGameState state, string from) =>
        state.LegalMoves.Where(m => new ChessPosition(m.From).ToString() == from)
            .Select(m => new ChessPosition(m.To).ToString()).Distinct();

    // ── Movement ──────────────────────────────────────────────

    [Fact]
    public void Pawn_MaySingleAndDoubleStep_OnlyFromStart()
    {
        var state = Start();
        Destinations(state, "e2").Should().BeEquivalentTo("e3", "e4");
        state = Sequence(state, "e2e3", "a7a6");
        Destinations(state, "e3").Should().BeEquivalentTo("e4");
    }

    [Fact]
    public void Pawn_CapturesDiagonally_NotForward()
    {
        var state = Sequence(Start(), "e2e4", "d7d5");
        Destinations(state, "e4").Should().BeEquivalentTo("e5", "d5");
        Reject(Sequence(Start(), "e2e4", "e7e5"), "e4", "e5").Should().Be(ErrorCodes.IllegalMove);
    }

    [Fact]
    public void Knight_JumpsOverPieces()
    {
        Destinations(Start(), "g1").Should().BeEquivalentTo("f3", "h3");
    }

    [Fact]
    public void SlidingPieces_AreObstructed()
    {
        Destinations(Start(), "a1").Should().BeEmpty();
        Destinations(Start(), "c1").Should().BeEmpty();
        Destinations(Start(), "d1").Should().BeEmpty();
        var open = Sequence(Start(), "e2e4", "e7e5");
        Destinations(open, "f1").Should().BeEquivalentTo("e2", "d3", "c4", "b5", "a6");
        Destinations(open, "d1").Should().BeEquivalentTo("e2", "f3", "g4", "h5");
    }

    [Fact]
    public void Rejects_UnknownSquares_AndWrongTurn()
    {
        var state = Start();
        Reject(state, "z9", "e4").Should().Be(ErrorCodes.InvalidAction);
        Engine.Apply(state, B, new ChessMoveAction("e7", "e5")).ErrorCode.Should().Be(ErrorCodes.NotYourTurn);
        Engine.Apply(state, "stranger", new ChessMoveAction("e2", "e4")).ErrorCode.Should().Be(ErrorCodes.NotYourTurn);
        Reject(state, "e2", "e5").Should().Be(ErrorCodes.IllegalMove);
    }

    // ── En passant and promotion ──────────────────────────────

    [Fact]
    public void EnPassant_IsAvailableOnlyImmediately_AndRemovesThePawn()
    {
        var state = Sequence(Start(), "e2e4", "a7a6", "e4e5", "d7d5");
        Destinations(state, "e5").Should().Contain("d6");
        var captured = Play(state, "e5", "d6");
        captured.Board[ChessPosition.From(3, 4).Index].IsEmpty.Should().BeTrue("the captured pawn on d5 is removed");
        captured.LastMove!.Captured.Type.Should().Be(PieceType.Pawn);
        captured.LastMove.San.Should().Be("exd6");

        var delayed = Sequence(state, "h2h3", "h7h6");
        Destinations(delayed, "e5").Should().NotContain("d6");
    }

    [Fact]
    public void Promotion_RequiresAChoice_AndAllFourPiecesWork()
    {
        const string fen = "7k/4P3/8/8/8/8/8/K7 w - - 0 1";
        Reject(Start(fen), "e7", "e8").Should().Be(ErrorCodes.PromotionRequired);
        Reject(Start(fen), "e7", "e8", "k").Should().Be(ErrorCodes.InvalidAction);
        foreach (var (letter, type) in new[] { ("q", PieceType.Queen), ("r", PieceType.Rook), ("b", PieceType.Bishop), ("n", PieceType.Knight) })
        {
            var promoted = Play(Start(fen), "e7", "e8", letter);
            promoted.Board[60].Type.Should().Be(type);
        }

        Engine.GetLegalActions(Start(fen), W).Count(a => a.To == "e8").Should().Be(4);
        Reject(Start("7k/8/8/8/8/8/4P3/K7 w - - 0 1"), "e2", "e3", "q").Should().Be(ErrorCodes.IllegalMove);
    }

    [Fact]
    public void Promotion_Capture_IsNotated()
    {
        var state = Start("3r3k/4P3/8/8/8/8/8/K7 w - - 0 1");
        Play(state, "e7", "d8", "q").LastMove!.San.Should().Be("exd8=Q+");
    }

    // ── Castling ──────────────────────────────────────────────

    private const string CastleReady = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

    [Fact]
    public void Castling_BothSides_MovesTheRook()
    {
        var kingside = Play(Start(CastleReady), "e1", "g1");
        kingside.Board[6].Type.Should().Be(PieceType.King);
        kingside.Board[5].Type.Should().Be(PieceType.Rook);
        kingside.LastMove!.San.Should().Be("O-O");

        var queenside = Play(Start(CastleReady), "e1", "c1");
        queenside.Board[2].Type.Should().Be(PieceType.King);
        queenside.Board[3].Type.Should().Be(PieceType.Rook);
        queenside.LastMove!.San.Should().Be("O-O-O");

        var black = Play(Play(Start(CastleReady), "a1", "a2"), "e8", "g8");
        black.Board[62].Type.Should().Be(PieceType.King);
        black.Board[61].Type.Should().Be(PieceType.Rook);
    }

    [Theory]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K1NR w KQkq - 0 1", "g1")] // piece between king and rook
    [InlineData("r3k2r/8/8/8/8/8/8/RN2K2R w KQkq - 0 1", "c1")] // piece on b1 blocks queenside
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w Qkq - 0 1", "g1")] // right already lost
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w Kkq - 0 1", "c1")]
    public void Castling_BlockedOrForbidden_Cases(string fen, string target) =>
        Destinations(Start(fen), "e1").Should().NotContain(target);

    [Theory]
    [InlineData("4k3/8/8/8/8/8/4r3/R3K2R w KQ - 0 1")] // in check
    [InlineData("4kr2/8/8/8/8/8/8/R3K2R w KQ - 0 1")] // f1 attacked (passes through)
    [InlineData("4k1r1/8/8/8/8/8/8/R3K2R w KQ - 0 1")] // g1 attacked (lands on)
    public void Castling_KingSide_NotAllowed_WhenCheckedOrThroughOrIntoAttack(string fen)
    {
        Destinations(Start(fen), "e1").Should().NotContain("g1");
    }

    [Fact]
    public void Castling_QueenSide_AttackedPassThroughSquares()
    {
        Destinations(Start("3rk3/8/8/8/8/8/8/R3K2R w KQ - 0 1"), "e1").Should().NotContain("c1", "d1 is attacked");
        Destinations(Start("2r1k3/8/8/8/8/8/8/R3K2R w KQ - 0 1"), "e1").Should().NotContain("c1", "c1 is attacked");
        // b1 may be attacked: only the king's path matters.
        Destinations(Start("1r2k3/8/8/8/8/8/8/R3K2R w KQ - 0 1"), "e1").Should().Contain("c1");
    }

    [Fact]
    public void Castling_RightsAreLost_AfterKingOrRookMoves()
    {
        var afterRook = Sequence(Start(CastleReady), "h1h2", "a8a7", "h2h1", "a7a8");
        Destinations(afterRook, "e1").Should().NotContain("g1").And.Contain("c1");

        var afterKing = Sequence(Start(CastleReady), "e1e2", "a8a7", "e2e1", "a7a8");
        Destinations(afterKing, "e1").Should().NotContain("g1").And.NotContain("c1");
    }

    // ── Check, pins, mate ─────────────────────────────────────

    [Fact]
    public void PinnedPiece_CannotLeaveTheLine()
    {
        var state = Start("4r1k1/8/8/8/8/8/4B3/4K3 w - - 0 1");
        Destinations(state, "e2").Should().BeEmpty("a bishop pinned on the e-file cannot move");

        var rookPinned = Start("4r1k1/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Destinations(rookPinned, "e2").Should().BeEquivalentTo("e3", "e4", "e5", "e6", "e7", "e8");
    }

    [Fact]
    public void InCheck_OnlyCheckResolvingMovesAreLegal()
    {
        var state = Start("4k3/8/8/8/8/8/3B4/r3K3 w - - 0 1");
        state.InCheck.Should().BeTrue();
        state.LegalMoves.Select(m => m.ToString()).Should().BeEquivalentTo("e1e2", "e1f2", "d2c1");
    }

    [Fact]
    public void CannotMoveKingIntoCheck()
    {
        var state = Start("4k3/8/8/8/8/8/8/r3K2R w - - 0 1");
        state.InCheck.Should().BeTrue();
        Destinations(state, "e1").Should().BeEquivalentTo("d2", "e2", "f2");
    }

    [Fact]
    public void FoolsMate_EndsTheGame_WithCheckmate()
    {
        var state = Sequence(Start(), "f2f3", "e7e5", "g2g4", "d8h4");
        state.Result.Should().NotBeNull();
        state.Result!.Outcome.Should().Be(GameOutcome.Win);
        state.Result.WinnerPlayerId.Should().Be(B);
        state.Result.Reason.Should().Be(GameEndReason.Checkmate);
        state.LastMove!.San.Should().Be("Qh4#");
        Engine.Apply(state, W, new ChessMoveAction("a2", "a3")).ErrorCode.Should().Be(ErrorCodes.GameOver);
    }

    [Fact]
    public void ScholarsMate_IsRecognised()
    {
        var state = Sequence(Start(), "e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6", "h5f7");
        state.Result!.WinnerPlayerId.Should().Be(W);
        state.Result.Reason.Should().Be(GameEndReason.Checkmate);
        state.LastMove!.San.Should().Be("Qxf7#");
    }

    [Fact]
    public void Stalemate_IsADraw()
    {
        var state = Start("7k/5Q2/8/8/8/8/8/K7 w - - 0 1");
        state = Play(state, "f7", "g6");
        state.Result.Should().NotBeNull();
        state.Result!.Outcome.Should().Be(GameOutcome.Draw);
        state.Result.Reason.Should().Be(GameEndReason.Stalemate);
    }

    // ── Draws ─────────────────────────────────────────────────

    [Theory]
    [InlineData("8/8/8/4k3/8/8/8/4K3 w - - 0 1", true)] // K v K
    [InlineData("8/8/8/4k3/8/8/8/3BK3 w - - 0 1", true)] // K+B v K
    [InlineData("8/8/8/4k3/8/8/8/3NK3 w - - 0 1", true)] // K+N v K
    [InlineData("8/8/8/4kb2/8/8/8/3BK3 w - - 0 1", true)] // bishops on same color (d1 and f5 are both light)
    [InlineData("8/8/8/4kb2/8/8/8/2B1K3 w - - 0 1", false)] // bishops on opposite colors
    [InlineData("8/8/8/4k3/8/8/8/3NNK2 w - - 0 1", false)] // two knights can (theoretically) mate
    [InlineData("8/8/8/4k3/8/8/4P3/4K3 w - - 0 1", false)] // pawn
    [InlineData("8/8/8/4k3/8/8/8/3RK3 w - - 0 1", false)] // rook
    [InlineData("8/8/8/4kn2/8/8/8/3BK3 w - - 0 1", false)] // B v N
    public void InsufficientMaterial(string fen, bool expected) =>
        DrawDetector.IsInsufficientMaterial(FenParser.Parse(fen)).Should().Be(expected);

    [Fact]
    public void CapturingDownToInsufficientMaterial_EndsTheGame()
    {
        var start = Start("4k3/8/8/8/8/8/3r4/2B1K3 w - - 0 1");
        start.Result.Should().BeNull();
        var result = Play(start, "c1", "d2").Result;
        result.Should().Be(GameResult.Draw(GameEndReason.InsufficientMaterial));
    }

    [Fact]
    public void ThreefoldRepetition_IsADraw()
    {
        var state = Start();
        var cycle = new[] { "g1f3", "g8f6", "f3g1", "f6g8" };
        state = Sequence(state, cycle);
        state.Result.Should().BeNull("the start position has now occurred twice");
        state = Sequence(state, cycle);
        state.Result.Should().NotBeNull();
        state.Result!.Outcome.Should().Be(GameOutcome.Draw);
        state.Result.Reason.Should().Be(GameEndReason.ThreefoldRepetition);
    }

    [Fact]
    public void FiftyMoveRule_DrawsAtHundredHalfmoves()
    {
        var almost = Start("4k3/8/8/8/8/8/8/R3K3 w - - 98 80");
        almost = Play(almost, "a1", "a2");
        almost.Result.Should().BeNull();
        var done = Play(almost, "e8", "d8");
        done.Result!.Reason.Should().Be(GameEndReason.FiftyMoveRule);
        done.Board.HalfmoveClock.Should().Be(100);
    }

    [Fact]
    public void Checkmate_TakesPrecedenceOver_FiftyMoveRule()
    {
        var state = Start("6k1/5ppp/8/8/8/8/8/R3K3 w - - 99 80");
        var mate = Play(state, "a1", "a8");
        mate.Result!.Reason.Should().Be(GameEndReason.Checkmate);
    }

    // ── Resign / timeout / FEN / SAN ──────────────────────────

    [Fact]
    public void Resign_GivesTheOpponentTheWin()
    {
        var result = ((IGameEngine)Engine).Resign(Start(), B);
        result.Should().Be(GameResult.Win(W, GameEndReason.Resignation));
    }

    [Fact]
    public void Timeout_LosesUnlessOpponentCannotMate()
    {
        Engine.DeclareTimeout(Start(), W).Should().Be(GameResult.Win(B, GameEndReason.Timeout));
        var lone = Start("8/8/8/4k3/8/8/8/3BK3 w - - 0 1");
        Engine.DeclareTimeout(lone, W).Outcome.Should().Be(GameOutcome.Draw, "a lone king cannot mate");
        Engine.DeclareTimeout(lone, B).Outcome.Should().Be(GameOutcome.Draw, "king and bishop cannot mate");
    }

    [Fact]
    public void Fen_RoundTrips()
    {
        foreach (var fen in new[]
        {
            FenParser.StartFen,
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
            "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3",
            "8/8/8/4k3/8/8/8/4K3 b - - 12 40",
        })
        {
            FenParser.ToFen(FenParser.Parse(fen)).Should().Be(fen);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a fen")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/P3K3 w - - 0 1")]
    public void Fen_RejectsGarbage(string fen) =>
        FenParser.TryParse(fen, out _, out _).Should().BeFalse();

    [Fact]
    public void San_DisambiguatesByFileRankOrBoth()
    {
        // Two knights can reach d2: disambiguate by file.
        var byFile = Play(Start("4k3/8/8/8/8/1N3N2/8/4K3 w - - 0 1"), "b3", "d2");
        byFile.LastMove!.San.Should().Be("Nbd2");

        // Two rooks on the same file: disambiguate by rank.
        var byRank = Play(Start("4k3/R7/8/8/8/8/R7/4K3 w - - 0 1"), "a2", "a4");
        byRank.LastMove!.San.Should().Be("R2a4");

        // Three queens: one needs both file and rank.
        var both = Play(Start("6k1/8/8/8/Q6Q/8/8/Q3K3 w - - 0 1"), "a4", "d4");
        both.LastMove!.San.Should().Be("Qa4d4");
    }

    [Fact]
    public void San_MovesAreRecordedInOrder()
    {
        var state = Sequence(Start(), "e2e4", "e7e5", "g1f3", "b8c6", "f1b5");
        state.Moves.Select(m => m.San).Should().Equal("e4", "e5", "Nf3", "Nc6", "Bb5");
    }

    [Fact]
    public void LegalActions_AreEmpty_WhenNotYourTurn_OrGameOver()
    {
        Engine.GetLegalActions(Start(), B).Should().BeEmpty();
        Engine.GetLegalActions(Start(), W).Should().HaveCount(20);
        var mate = Sequence(Start(), "f2f3", "e7e5", "g2g4", "d8h4");
        Engine.GetLegalActions(mate, W).Should().BeEmpty();
    }

    [Fact]
    public void Json_ActionsAreParsed_AndGarbageIsRejected()
    {
        IGameEngine engine = Engine;
        var state = engine.CreateInitialState([W, B]);
        var ok = engine.ApplyAction(state, W, """{"from":"e2","to":"e4"}""");
        ok.Success.Should().BeTrue();
        engine.GetCurrentTurnPlayerId(ok.NewState!).Should().Be(B);

        engine.ApplyAction(state, W, "not json").ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        engine.ApplyAction(state, W, "null").ErrorCode.Should().Be(ErrorCodes.InvalidAction);
        engine.ApplyAction(state, W, """{"from":"e2"}""").ErrorCode.Should().Be(ErrorCodes.InvalidAction);
    }
}
