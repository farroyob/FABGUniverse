using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;
using FabGames.Games.Checkers.Models;

namespace FabGames.Games.Checkers.Logic;

/// <summary>
/// Move generation for English draughts. Captures are mandatory and every capture sequence must be
/// completed; the player is free to choose among sequences (no "maximum capture" rule).
/// </summary>
public static class MandatoryCaptureRules
{
    private static readonly (int File, int Rank)[] Diagonals = [(1, 1), (-1, 1), (1, -1), (-1, -1)];

    /// <summary>All legal turns for <paramref name="color"/>. If any capture exists, only captures are returned.</summary>
    public static List<CheckersMove> GenerateLegal(CheckersBoard board, PlayerColor color)
    {
        var captures = new List<CheckersMove>();
        for (var sq = 0; sq < 64; sq++)
        {
            var piece = board[sq];
            if (!piece.IsEmpty && piece.Color == color)
            {
                CollectCaptures(board, sq, piece, captures);
            }
        }

        if (captures.Count > 0)
        {
            return captures;
        }

        var steps = new List<CheckersMove>();
        for (var sq = 0; sq < 64; sq++)
        {
            var piece = board[sq];
            if (!piece.IsEmpty && piece.Color == color)
            {
                CollectSteps(board, sq, piece, steps);
            }
        }

        return steps;
    }

    public static bool HasCapture(CheckersBoard board, PlayerColor color) =>
        GenerateLegal(board, color) is [{ IsCapture: true }, ..];

    private static void CollectSteps(CheckersBoard board, int from, CheckersPiece piece, List<CheckersMove> moves)
    {
        var file = from & 7;
        var rank = from >> 3;
        foreach (var (df, dr) in Directions(piece))
        {
            if (!TryOffset(file, rank, df, dr, out var to) || !board[to].IsEmpty)
            {
                continue;
            }

            moves.Add(new CheckersMove(from, [to], [], KingPromotion.Promotes(piece, to)));
        }
    }

    private static void CollectCaptures(CheckersBoard board, int origin, CheckersPiece piece, List<CheckersMove> results)
    {
        var path = new List<int>();
        var captured = new List<int>();
        Extend(board, origin, piece, origin, path, captured, results);
    }

    private static void Extend(
        CheckersBoard board, int origin, CheckersPiece piece, int current,
        List<int> path, List<int> captured, List<CheckersMove> results)
    {
        var extended = false;
        var file = current & 7;
        var rank = current >> 3;
        foreach (var (df, dr) in Directions(piece))
        {
            if (!TryOffset(file, rank, df, dr, out var over)
                || !TryOffset(over & 7, over >> 3, df, dr, out var landing))
            {
                continue;
            }

            var victim = board[over];
            var landingFree = board[landing].IsEmpty || landing == origin;
            if (victim.IsEmpty || victim.Color == piece.Color || captured.Contains(over) || !landingFree)
            {
                continue;
            }

            extended = true;
            path.Add(landing);
            captured.Add(over);
            if (KingPromotion.Promotes(piece, landing))
            {
                // A man that is crowned mid-capture ends its turn there.
                results.Add(new CheckersMove(origin, [.. path], [.. captured], true));
            }
            else
            {
                Extend(board, origin, piece, landing, path, captured, results);
            }

            path.RemoveAt(path.Count - 1);
            captured.RemoveAt(captured.Count - 1);
        }

        if (!extended && path.Count > 0)
        {
            results.Add(new CheckersMove(origin, [.. path], [.. captured], false));
        }
    }

    private static IEnumerable<(int File, int Rank)> Directions(CheckersPiece piece)
    {
        if (piece.IsKing)
        {
            return Diagonals;
        }

        // Men advance only: light toward rank 8, dark toward rank 1.
        var forward = piece.Color == PlayerColor.White ? 1 : -1;
        return [(1, forward), (-1, forward)];
    }

    private static bool TryOffset(int file, int rank, int df, int dr, out int square)
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

public static class KingPromotion
{
    /// <summary>True if a man of this color landing on <paramref name="square"/> is crowned.</summary>
    public static bool Promotes(CheckersPiece piece, int square) =>
        piece.Type == CheckersPieceType.Man && (square >> 3) == (piece.Color == PlayerColor.White ? 7 : 0);
}
