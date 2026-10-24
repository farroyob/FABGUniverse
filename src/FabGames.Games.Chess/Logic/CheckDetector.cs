using FabGames.Core.Domain.Enums;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

public static class CheckDetector
{
    internal static readonly (int File, int Rank)[] KnightDeltas =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

    internal static readonly (int File, int Rank)[] KingDeltas =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    internal static readonly (int File, int Rank)[] DiagonalDirs = [(1, 1), (-1, 1), (-1, -1), (1, -1)];

    internal static readonly (int File, int Rank)[] OrthogonalDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static bool IsInCheck(ChessBoard board, PlayerColor color)
    {
        var king = board.KingSquare(color);
        return king >= 0 && IsSquareAttacked(board, king, color.Opposite());
    }

    /// <summary>True if any piece of <paramref name="by"/> attacks <paramref name="square"/>.</summary>
    public static bool IsSquareAttacked(ChessBoard board, int square, PlayerColor by)
    {
        var file = square & 7;
        var rank = square >> 3;

        var pawnRank = rank - (by == PlayerColor.White ? 1 : -1);
        if (pawnRank is >= 0 and <= 7)
        {
            for (var df = -1; df <= 1; df += 2)
            {
                var f = file + df;
                if (f is >= 0 and <= 7 && IsPiece(board[(pawnRank << 3) | f], PieceType.Pawn, by))
                {
                    return true;
                }
            }
        }

        foreach (var (df, dr) in KnightDeltas)
        {
            if (TryOffset(file, rank, df, dr, out var sq) && IsPiece(board[sq], PieceType.Knight, by))
            {
                return true;
            }
        }

        foreach (var (df, dr) in KingDeltas)
        {
            if (TryOffset(file, rank, df, dr, out var sq) && IsPiece(board[sq], PieceType.King, by))
            {
                return true;
            }
        }

        return AttackedAlong(board, file, rank, by, DiagonalDirs, PieceType.Bishop)
            || AttackedAlong(board, file, rank, by, OrthogonalDirs, PieceType.Rook);
    }

    private static bool AttackedAlong(
        ChessBoard board, int file, int rank, PlayerColor by, (int File, int Rank)[] dirs, PieceType slider)
    {
        foreach (var (df, dr) in dirs)
        {
            var f = file + df;
            var r = rank + dr;
            while (f is >= 0 and <= 7 && r is >= 0 and <= 7)
            {
                var piece = board[(r << 3) | f];
                if (!piece.IsEmpty)
                {
                    if (piece.Color == by && (piece.Type == slider || piece.Type == PieceType.Queen))
                    {
                        return true;
                    }

                    break;
                }

                f += df;
                r += dr;
            }
        }

        return false;
    }

    private static bool IsPiece(ChessPiece piece, PieceType type, PlayerColor color) =>
        piece.Type == type && piece.Color == color;

    internal static bool TryOffset(int file, int rank, int df, int dr, out int square)
    {
        var f = file + df;
        var r = rank + dr;
        if (f is < 0 or > 7 || r is < 0 or > 7)
        {
            square = -1;
            return false;
        }

        square = (r << 3) | f;
        return true;
    }
}
