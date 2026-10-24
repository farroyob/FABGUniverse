using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;
using FabGames.Core.Services;
using FabGames.Games.Chess.Logic;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess;

/// <summary>Complete chess rules. Index 0 of the player list plays White, who moves first.</summary>
public sealed class ChessEngine : GameEngineBase<ChessGameState, ChessMoveAction>
{
    public const string Id = "chess";

    public override string GameTypeId => Id;

    public override int MinPlayers => 2;

    public override int MaxPlayers => 2;

    public override ChessGameState NewGame(IReadOnlyList<string> playerIdsByColor) =>
        NewGameFromFen(FenParser.StartFen, playerIdsByColor);

    /// <summary>Starts a game from an arbitrary position (also used by tests).</summary>
    public ChessGameState NewGameFromFen(string fen, IReadOnlyList<string> playerIdsByColor)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(playerIdsByColor.Count, 2);
        var board = FenParser.Parse(fen);
        return Build(board, playerIdsByColor[0], playerIdsByColor[1], [], []);
    }

    public override GameActionResult<ChessGameState> Apply(ChessGameState state, string playerId, ChessMoveAction action)
    {
        if (state.Result is not null)
        {
            return GameActionResult<ChessGameState>.Fail(ErrorCodes.GameOver);
        }

        if (state.PlayerIdOf(state.Board.ActiveColor) != playerId)
        {
            return GameActionResult<ChessGameState>.Fail(ErrorCodes.NotYourTurn);
        }

        if (!MoveValidator.TryResolve(state.LegalMoves, action, out var move, out var error))
        {
            return GameActionResult<ChessGameState>.Fail(error);
        }

        var board = state.Board;
        var captured = move.IsEnPassant
            ? new ChessPiece(PieceType.Pawn, board.ActiveColor.Opposite())
            : board[move.To];
        var record = new ChessMoveRecord(
            state.Moves.Count + 1,
            board.ActiveColor,
            SanFormatter.Format(board, move, state.LegalMoves),
            new ChessPosition(move.From),
            new ChessPosition(move.To),
            board[move.From],
            captured,
            move.Promotion,
            move.Flags);

        var next = Build(
            board.Apply(move), state.WhitePlayerId, state.BlackPlayerId, state.PositionKeys, state.Moves.Add(record));
        return GameActionResult<ChessGameState>.Ok(next, next.Result);
    }

    public override string GetCurrentTurnPlayerId(ChessGameState state) =>
        state.PlayerIdOf(state.Board.ActiveColor);

    public override IReadOnlyList<ChessMoveAction> GetLegalActions(ChessGameState state, string playerId)
    {
        if (state.Result is not null || state.PlayerIdOf(state.Board.ActiveColor) != playerId)
        {
            return [];
        }

        return [.. state.LegalMoves.Select(MoveValidator.ToAction)];
    }

    public override GameResult? GetResult(ChessGameState state) => state.Result;

    public override GameResult Resign(ChessGameState state, string playerId) =>
        GameResult.Win(OpponentOf(state, playerId), GameEndReason.Resignation);

    /// <summary>A flag fall loses, unless the opponent could never mate, which is a draw.</summary>
    public override GameResult DeclareTimeout(ChessGameState state, string playerId)
    {
        var opponentColor = (state.ColorOf(playerId) ?? PlayerColor.White).Opposite();
        return DrawDetector.HasMatingMaterial(state.Board, opponentColor)
            ? GameResult.Win(OpponentOf(state, playerId), GameEndReason.Timeout)
            : GameResult.Draw(GameEndReason.InsufficientMaterial);
    }

    private static string OpponentOf(ChessGameState state, string playerId) =>
        playerId == state.WhitePlayerId ? state.BlackPlayerId : state.WhitePlayerId;

    private static ChessGameState Build(
        ChessBoard board,
        string whiteId,
        string blackId,
        ImmutableList<string> previousKeys,
        ImmutableList<ChessMoveRecord> moves)
    {
        var legal = MoveGenerator.GenerateLegal(board);
        var inCheck = CheckDetector.IsInCheck(board, board.ActiveColor);
        var keys = previousKeys.Add(DrawDetector.PositionKey(board, legal));

        GameResult? result = null;
        if (legal.Count == 0)
        {
            var mover = board.ActiveColor.Opposite() == PlayerColor.White ? whiteId : blackId;
            result = inCheck
                ? GameResult.Win(mover, GameEndReason.Checkmate)
                : GameResult.Draw(GameEndReason.Stalemate);
        }
        else if (DrawDetector.IsInsufficientMaterial(board))
        {
            result = GameResult.Draw(GameEndReason.InsufficientMaterial);
        }
        else if (DrawDetector.IsThreefoldRepetition(keys))
        {
            result = GameResult.Draw(GameEndReason.ThreefoldRepetition);
        }
        else if (DrawDetector.IsFiftyMoveRule(board))
        {
            result = GameResult.Draw(GameEndReason.FiftyMoveRule);
        }

        return new ChessGameState(board, whiteId, blackId, [.. legal], inCheck, keys, moves, result);
    }
}
