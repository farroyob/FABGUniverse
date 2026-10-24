using System.Text;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

/// <summary>Formats moves in Standard Algebraic Notation (for example <c>Nbd7</c>, <c>exd5</c>, <c>O-O</c>, <c>e8=Q#</c>).</summary>
public static class SanFormatter
{
    /// <param name="before">Position before the move.</param>
    /// <param name="move">The move being played.</param>
    /// <param name="legalMoves">All legal moves in <paramref name="before"/>, used for disambiguation.</param>
    public static string Format(ChessBoard before, ChessMove move, IReadOnlyList<ChessMove> legalMoves)
    {
        var sb = new StringBuilder(8);
        var piece = before[move.From];
        var from = new ChessPosition(move.From);

        if ((move.Flags & MoveFlags.KingsideCastle) != 0)
        {
            sb.Append("O-O");
        }
        else if ((move.Flags & MoveFlags.QueensideCastle) != 0)
        {
            sb.Append("O-O-O");
        }
        else
        {
            if (piece.Type == PieceType.Pawn)
            {
                if (move.IsCapture)
                {
                    sb.Append((char)('a' + from.File));
                }
            }
            else
            {
                sb.Append(char.ToUpperInvariant(piece.ToFenChar()));
                AppendDisambiguation(sb, before, piece, move, from, legalMoves);
            }

            if (move.IsCapture)
            {
                sb.Append('x');
            }

            sb.Append(new ChessPosition(move.To));
            if (move.Promotion != PieceType.None)
            {
                sb.Append('=').Append(char.ToUpperInvariant(ChessMove.PromotionLetter(move.Promotion)[0]));
            }
        }

        var after = before.Apply(move);
        if (CheckDetector.IsInCheck(after, after.ActiveColor))
        {
            sb.Append(MoveGenerator.GenerateLegal(after).Count == 0 ? '#' : '+');
        }

        return sb.ToString();
    }

    private static void AppendDisambiguation(
        StringBuilder sb, ChessBoard before, ChessPiece piece, ChessMove move, ChessPosition from,
        IReadOnlyList<ChessMove> legalMoves)
    {
        var ambiguous = false;
        var sameFile = false;
        var sameRank = false;
        foreach (var other in legalMoves)
        {
            if (other.To != move.To || other.From == move.From || before[other.From] != piece)
            {
                continue;
            }

            var otherFrom = new ChessPosition(other.From);
            ambiguous = true;
            sameFile |= otherFrom.File == from.File;
            sameRank |= otherFrom.Rank == from.Rank;
        }

        if (!ambiguous)
        {
            return;
        }

        if (!sameFile)
        {
            sb.Append((char)('a' + from.File));
        }
        else if (!sameRank)
        {
            sb.Append(from.Rank + 1);
        }
        else
        {
            sb.Append(from);
        }
    }
}
