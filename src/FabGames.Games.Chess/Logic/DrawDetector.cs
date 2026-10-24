using FabGames.Core.Domain.Enums;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

public static class DrawDetector
{
    public const int FiftyMoveHalfmoves = 100;

    /// <summary>
    /// Dead position by material: K v K, K+B v K, K+N v K, or only bishops that all stand on squares of one color.
    /// </summary>
    public static bool IsInsufficientMaterial(ChessBoard board)
    {
        var knights = 0;
        var lightBishops = 0;
        var darkBishops = 0;
        for (var sq = 0; sq < 64; sq++)
        {
            switch (board[sq].Type)
            {
                case PieceType.Pawn or PieceType.Rook or PieceType.Queen:
                    return false;
                case PieceType.Knight:
                    knights++;
                    break;
                case PieceType.Bishop:
                    if (((sq & 7) + (sq >> 3)) % 2 == 0)
                    {
                        darkBishops++;
                    }
                    else
                    {
                        lightBishops++;
                    }

                    break;
            }
        }

        var bishops = lightBishops + darkBishops;
        if (knights == 0)
        {
            return bishops == 0 || lightBishops == 0 || darkBishops == 0;
        }

        return knights == 1 && bishops == 0;
    }

    /// <summary>
    /// Whether <paramref name="color"/> could still deliver mate with some cooperation: any pawn, rook,
    /// queen, or at least two minor pieces. Used to decide timeout results.
    /// </summary>
    public static bool HasMatingMaterial(ChessBoard board, PlayerColor color)
    {
        var minors = 0;
        for (var sq = 0; sq < 64; sq++)
        {
            var piece = board[sq];
            if (piece.IsEmpty || piece.Color != color)
            {
                continue;
            }

            switch (piece.Type)
            {
                case PieceType.Pawn or PieceType.Rook or PieceType.Queen:
                    return true;
                case PieceType.Knight or PieceType.Bishop:
                    minors++;
                    break;
            }
        }

        return minors >= 2;
    }

    public static bool IsFiftyMoveRule(ChessBoard board) => board.HalfmoveClock >= FiftyMoveHalfmoves;

    public static bool IsThreefoldRepetition(IReadOnlyList<string> positionKeys)
    {
        if (positionKeys.Count < 5)
        {
            return false;
        }

        var current = positionKeys[^1];
        var count = 0;
        for (var i = positionKeys.Count - 1; i >= 0; i -= 2)
        {
            if (positionKeys[i] == current && ++count >= 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Identity of a position for repetition: placement, side, castling rights and en passant square
    /// (the latter only when an en passant capture is actually legal).
    /// </summary>
    public static string PositionKey(ChessBoard board, IReadOnlyList<ChessMove> legalMoves)
    {
        var enPassant = false;
        foreach (var move in legalMoves)
        {
            if (move.IsEnPassant)
            {
                enPassant = true;
                break;
            }
        }

        return $"{FenParser.Placement(board)} {(board.ActiveColor == PlayerColor.White ? 'w' : 'b')} " +
               $"{FenParser.CastlingField(board.Castling)} {(enPassant ? board.EnPassantSquare : -1)}";
    }
}
