using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Domain.Results;

namespace FabGames.Games.Checkers.Models;

public sealed record CheckersMoveRecord(
    int Ply, PlayerColor Color, string Notation, CheckersMove Move, CheckersPiece Piece);

/// <summary>Immutable snapshot of a checkers game.</summary>
public sealed record CheckersGameState(
    CheckersBoard Board,
    PlayerColor ActiveColor,
    string WhitePlayerId,
    string BlackPlayerId,
    ImmutableArray<CheckersMove> LegalMoves,
    int PliesSinceProgress,
    ImmutableList<CheckersMoveRecord> Moves,
    GameResult? Result)
{
    public CheckersMoveRecord? LastMove => Moves.Count == 0 ? null : Moves[^1];

    /// <summary>True when the side to move is obliged to capture.</summary>
    public bool MustCapture => !LegalMoves.IsDefaultOrEmpty && LegalMoves[0].IsCapture;

    public string PlayerIdOf(PlayerColor color) => color == PlayerColor.White ? WhitePlayerId : BlackPlayerId;
}
