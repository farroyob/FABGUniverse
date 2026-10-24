using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;
using FabGames.Core.Services;
using FabGames.Games.Checkers.Logic;
using FabGames.Games.Checkers.Models;

namespace FabGames.Games.Checkers;

/// <summary>
/// English draughts (American checkers). Index 0 of the player list plays the light side, index 1 the
/// dark side. Dark moves first, as in the official rules.
/// </summary>
public sealed class CheckersEngine : GameEngineBase<CheckersGameState, CheckersMoveAction>
{
    public const string Id = "checkers";

    /// <summary>The side that makes the first move.</summary>
    public const PlayerColor FirstMover = PlayerColor.Black;

    private readonly int _drawMoveLimitPerSide;

    public CheckersEngine()
        : this(DrawDetector.DefaultMoveLimitPerSide)
    {
    }

    public CheckersEngine(int drawMoveLimitPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(drawMoveLimitPerSide, 1);
        _drawMoveLimitPerSide = drawMoveLimitPerSide;
    }

    public override string GameTypeId => Id;

    public override int MinPlayers => 2;

    public override int MaxPlayers => 2;

    public override CheckersGameState NewGame(IReadOnlyList<string> playerIdsByColor) =>
        NewGameFromBoard(CheckersBoard.CreateStart(), FirstMover, playerIdsByColor);

    /// <summary>Starts from an arbitrary position (also used by tests).</summary>
    public CheckersGameState NewGameFromBoard(
        CheckersBoard board, PlayerColor activeColor, IReadOnlyList<string> playerIdsByColor)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(playerIdsByColor.Count, 2);
        return Build(board, activeColor, playerIdsByColor[0], playerIdsByColor[1], 0, []);
    }

    public override GameActionResult<CheckersGameState> Apply(
        CheckersGameState state, string playerId, CheckersMoveAction action)
    {
        if (state.Result is not null)
        {
            return GameActionResult<CheckersGameState>.Fail(ErrorCodes.GameOver);
        }

        if (state.PlayerIdOf(state.ActiveColor) != playerId)
        {
            return GameActionResult<CheckersGameState>.Fail(ErrorCodes.NotYourTurn);
        }

        if (!CheckersValidator.TryResolve(state.LegalMoves, action, out var move, out var error))
        {
            return GameActionResult<CheckersGameState>.Fail(error);
        }

        var piece = state.Board[move.From];
        var record = new CheckersMoveRecord(state.Moves.Count + 1, state.ActiveColor, move.ToNotation(), move, piece);
        var progressed = move.IsCapture || piece.Type == CheckersPieceType.Man;
        var next = Build(
            state.Board.Apply(move),
            state.ActiveColor.Opposite(),
            state.WhitePlayerId,
            state.BlackPlayerId,
            progressed ? 0 : state.PliesSinceProgress + 1,
            state.Moves.Add(record));
        return GameActionResult<CheckersGameState>.Ok(next, next.Result);
    }

    public override string GetCurrentTurnPlayerId(CheckersGameState state) => state.PlayerIdOf(state.ActiveColor);

    public override IReadOnlyList<CheckersMoveAction> GetLegalActions(CheckersGameState state, string playerId)
    {
        if (state.Result is not null || state.PlayerIdOf(state.ActiveColor) != playerId)
        {
            return [];
        }

        return [.. state.LegalMoves.Select(m => m.ToAction())];
    }

    public override GameResult? GetResult(CheckersGameState state) => state.Result;

    public override GameResult Resign(CheckersGameState state, string playerId) =>
        GameResult.Win(OpponentOf(state, playerId), GameEndReason.Resignation);

    public override GameResult DeclareTimeout(CheckersGameState state, string playerId) =>
        GameResult.Win(OpponentOf(state, playerId), GameEndReason.Timeout);

    private static string OpponentOf(CheckersGameState state, string playerId) =>
        playerId == state.WhitePlayerId ? state.BlackPlayerId : state.WhitePlayerId;

    private CheckersGameState Build(
        CheckersBoard board,
        PlayerColor active,
        string whiteId,
        string blackId,
        int pliesSinceProgress,
        ImmutableList<CheckersMoveRecord> moves)
    {
        var legal = MandatoryCaptureRules.GenerateLegal(board, active);
        var opponentId = active == PlayerColor.White ? blackId : whiteId;

        GameResult? result = null;
        if (board.Count(active) == 0)
        {
            result = GameResult.Win(opponentId, GameEndReason.NoPieces);
        }
        else if (legal.Count == 0)
        {
            result = GameResult.Win(opponentId, GameEndReason.NoLegalMoves);
        }
        else if (DrawDetector.IsDrawn(pliesSinceProgress, _drawMoveLimitPerSide))
        {
            result = GameResult.Draw(GameEndReason.FortyMoveRule);
        }

        return new CheckersGameState(board, active, whiteId, blackId, [.. legal], pliesSinceProgress, moves, result);
    }
}
