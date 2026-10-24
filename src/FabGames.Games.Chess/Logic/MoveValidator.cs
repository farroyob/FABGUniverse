using FabGames.Core.Domain.Results;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

/// <summary>Maps a client action onto one of the legal moves, or explains why it cannot.</summary>
public static class MoveValidator
{
    public static bool TryResolve(
        IReadOnlyList<ChessMove> legalMoves, ChessMoveAction action, out ChessMove move, out string errorCode)
    {
        move = default;
        if (!ChessPosition.TryParse(action.From, out var from)
            || !ChessPosition.TryParse(action.To, out var to)
            || !TryParsePromotion(action.Promotion, out var promotion))
        {
            errorCode = ErrorCodes.InvalidAction;
            return false;
        }

        var found = false;
        var needsPromotion = false;
        foreach (var candidate in legalMoves)
        {
            if (candidate.From != from.Index || candidate.To != to.Index)
            {
                continue;
            }

            needsPromotion |= candidate.Promotion != PieceType.None;
            if (candidate.Promotion == promotion)
            {
                move = candidate;
                found = true;
            }
        }

        if (found)
        {
            errorCode = string.Empty;
            return true;
        }

        errorCode = needsPromotion && promotion == PieceType.None
            ? ErrorCodes.PromotionRequired
            : ErrorCodes.IllegalMove;
        return false;
    }

    public static ChessMoveAction ToAction(ChessMove move) =>
        new(
            new ChessPosition(move.From).ToString(),
            new ChessPosition(move.To).ToString(),
            move.Promotion == PieceType.None ? null : ChessMove.PromotionLetter(move.Promotion));

    private static bool TryParsePromotion(string? text, out PieceType promotion)
    {
        promotion = PieceType.None;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        promotion = text.ToLowerInvariant() switch
        {
            "q" => PieceType.Queen,
            "r" => PieceType.Rook,
            "b" => PieceType.Bishop,
            "n" => PieceType.Knight,
            _ => PieceType.None,
        };
        return promotion != PieceType.None;
    }
}
