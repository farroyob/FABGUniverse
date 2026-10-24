using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;

namespace FabGames.Games.Chess.Models;

/// <summary>One played move, kept for the move list and captured-piece display.</summary>
public sealed record ChessMoveRecord(
    int Ply,
    PlayerColor Color,
    string San,
    ChessPosition From,
    ChessPosition To,
    ChessPiece Piece,
    ChessPiece Captured,
    PieceType Promotion,
    MoveFlags Flags);

/// <summary>Immutable snapshot of a chess game.</summary>
public sealed record ChessGameState(
    ChessBoard Board,
    string WhitePlayerId,
    string BlackPlayerId,
    ImmutableArray<ChessMove> LegalMoves,
    bool InCheck,
    ImmutableList<string> PositionKeys,
    ImmutableList<ChessMoveRecord> Moves,
    GameResult? Result)
{
    public ChessMoveRecord? LastMove => Moves.Count == 0 ? null : Moves[^1];

    public string PlayerIdOf(PlayerColor color) => color == PlayerColor.White ? WhitePlayerId : BlackPlayerId;

    public PlayerColor? ColorOf(string playerId) =>
        playerId == WhitePlayerId ? PlayerColor.White
        : playerId == BlackPlayerId ? PlayerColor.Black
        : null;
}
