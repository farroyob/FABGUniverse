using FabGames.Core.Domain.Enums;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

public static class MoveGenerator
{
    private static readonly PieceType[] PromotionTypes =
        [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    /// <summary>All legal moves for the side to move.</summary>
    public static List<ChessMove> GenerateLegal(ChessBoard board)
    {
        var pseudo = new List<ChessMove>(48);
        GeneratePseudoLegal(board, pseudo);

        var us = board.ActiveColor;
        var legal = new List<ChessMove>(pseudo.Count);
        foreach (var move in pseudo)
        {
            if (!CheckDetector.IsInCheck(board.Apply(move), us))
            {
                legal.Add(move);
            }
        }

        return legal;
    }

    /// <summary>Counts leaf nodes of the legal move tree; used to validate the generator.</summary>
    public static long Perft(ChessBoard board, int depth)
    {
        if (depth == 0)
        {
            return 1;
        }

        var moves = GenerateLegal(board);
        if (depth == 1)
        {
            return moves.Count;
        }

        long nodes = 0;
        foreach (var move in moves)
        {
            nodes += Perft(board.Apply(move), depth - 1);
        }

        return nodes;
    }

    private static void GeneratePseudoLegal(ChessBoard board, List<ChessMove> moves)
    {
        var us = board.ActiveColor;
        for (var square = 0; square < 64; square++)
        {
            var piece = board[square];
            if (piece.IsEmpty || piece.Color != us)
            {
                continue;
            }

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    GeneratePawn(board, square, us, moves);
                    break;
                case PieceType.Knight:
                    GenerateSteps(board, square, us, CheckDetector.KnightDeltas, moves);
                    break;
                case PieceType.Bishop:
                    GenerateSlides(board, square, us, CheckDetector.DiagonalDirs, moves);
                    break;
                case PieceType.Rook:
                    GenerateSlides(board, square, us, CheckDetector.OrthogonalDirs, moves);
                    break;
                case PieceType.Queen:
                    GenerateSlides(board, square, us, CheckDetector.DiagonalDirs, moves);
                    GenerateSlides(board, square, us, CheckDetector.OrthogonalDirs, moves);
                    break;
                case PieceType.King:
                    GenerateSteps(board, square, us, CheckDetector.KingDeltas, moves);
                    GenerateCastling(board, square, us, moves);
                    break;
            }
        }
    }

    private static void GeneratePawn(ChessBoard board, int from, PlayerColor us, List<ChessMove> moves)
    {
        var dir = us == PlayerColor.White ? 1 : -1;
        var startRank = us == PlayerColor.White ? 1 : 6;
        var file = from & 7;
        var rank = from >> 3;

        var one = from + (8 * dir);
        if (board[one].IsEmpty)
        {
            AddPawnMove(moves, from, one, MoveFlags.None);
            var two = one + (8 * dir);
            if (rank == startRank && board[two].IsEmpty)
            {
                moves.Add(new ChessMove(from, two, PieceType.None, MoveFlags.DoublePush));
            }
        }

        for (var df = -1; df <= 1; df += 2)
        {
            var f = file + df;
            if (f is < 0 or > 7)
            {
                continue;
            }

            var to = ((rank + dir) << 3) | f;
            var target = board[to];
            if (!target.IsEmpty && target.Color != us)
            {
                AddPawnMove(moves, from, to, MoveFlags.Capture);
            }
            else if (target.IsEmpty && to == board.EnPassantSquare)
            {
                moves.Add(new ChessMove(from, to, PieceType.None, MoveFlags.Capture | MoveFlags.EnPassant));
            }
        }
    }

    private static void AddPawnMove(List<ChessMove> moves, int from, int to, MoveFlags flags)
    {
        var lastRank = (to >> 3) is 0 or 7;
        if (!lastRank)
        {
            moves.Add(new ChessMove(from, to, PieceType.None, flags));
            return;
        }

        foreach (var promotion in PromotionTypes)
        {
            moves.Add(new ChessMove(from, to, promotion, flags));
        }
    }

    private static void GenerateSteps(
        ChessBoard board, int from, PlayerColor us, (int File, int Rank)[] deltas, List<ChessMove> moves)
    {
        var file = from & 7;
        var rank = from >> 3;
        foreach (var (df, dr) in deltas)
        {
            if (!CheckDetector.TryOffset(file, rank, df, dr, out var to))
            {
                continue;
            }

            var target = board[to];
            if (target.IsEmpty)
            {
                moves.Add(new ChessMove(from, to));
            }
            else if (target.Color != us)
            {
                moves.Add(new ChessMove(from, to, PieceType.None, MoveFlags.Capture));
            }
        }
    }

    private static void GenerateSlides(
        ChessBoard board, int from, PlayerColor us, (int File, int Rank)[] dirs, List<ChessMove> moves)
    {
        var file = from & 7;
        var rank = from >> 3;
        foreach (var (df, dr) in dirs)
        {
            var f = file + df;
            var r = rank + dr;
            while (f is >= 0 and <= 7 && r is >= 0 and <= 7)
            {
                var to = (r << 3) | f;
                var target = board[to];
                if (target.IsEmpty)
                {
                    moves.Add(new ChessMove(from, to));
                }
                else
                {
                    if (target.Color != us)
                    {
                        moves.Add(new ChessMove(from, to, PieceType.None, MoveFlags.Capture));
                    }

                    break;
                }

                f += df;
                r += dr;
            }
        }
    }

    private static void GenerateCastling(ChessBoard board, int from, PlayerColor us, List<ChessMove> moves)
    {
        var white = us == PlayerColor.White;
        var home = white ? 4 : 60;
        if (from != home)
        {
            return;
        }

        var them = us.Opposite();
        var kingside = white ? CastlingRights.WhiteKingside : CastlingRights.BlackKingside;
        var queenside = white ? CastlingRights.WhiteQueenside : CastlingRights.BlackQueenside;
        var rook = new ChessPiece(PieceType.Rook, us);
        var inCheck = (board.Castling & (kingside | queenside)) != 0 && CheckDetector.IsSquareAttacked(board, home, them);
        if (inCheck)
        {
            return;
        }

        if ((board.Castling & kingside) != 0
            && board[home + 3] == rook
            && board[home + 1].IsEmpty && board[home + 2].IsEmpty
            && !CheckDetector.IsSquareAttacked(board, home + 1, them)
            && !CheckDetector.IsSquareAttacked(board, home + 2, them))
        {
            moves.Add(new ChessMove(home, home + 2, PieceType.None, MoveFlags.KingsideCastle));
        }

        if ((board.Castling & queenside) != 0
            && board[home - 4] == rook
            && board[home - 1].IsEmpty && board[home - 2].IsEmpty && board[home - 3].IsEmpty
            && !CheckDetector.IsSquareAttacked(board, home - 1, them)
            && !CheckDetector.IsSquareAttacked(board, home - 2, them))
        {
            moves.Add(new ChessMove(home, home - 2, PieceType.None, MoveFlags.QueensideCastle));
        }
    }
}
