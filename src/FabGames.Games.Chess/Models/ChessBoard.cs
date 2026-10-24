using FabGames.Core.Domain.Enums;
using FabGames.Games.Chess.Logic;

namespace FabGames.Games.Chess.Models;

/// <summary>
/// Board position (placement, side to move, castling, en passant, clocks). Instances are treated as
/// immutable once published: <see cref="Apply"/> returns a new board.
/// </summary>
public sealed class ChessBoard
{
    private readonly ChessPiece[] _squares = new ChessPiece[64];
    private readonly int[] _kings = [-1, -1];

    public PlayerColor ActiveColor { get; private set; } = PlayerColor.White;

    public CastlingRights Castling { get; private set; } = CastlingRights.None;

    /// <summary>Square a pawn may capture onto en passant, or -1.</summary>
    public int EnPassantSquare { get; private set; } = -1;

    /// <summary>Plies since the last capture or pawn move.</summary>
    public int HalfmoveClock { get; private set; }

    public int FullmoveNumber { get; private set; } = 1;

    public ChessPiece this[int square] => _squares[square];

    public int KingSquare(PlayerColor color) => _kings[(int)color];

    public static ChessBoard CreateStart() => FenParser.Parse(FenParser.StartFen);

    internal void Place(int square, ChessPiece piece)
    {
        _squares[square] = piece;
        if (piece.Type == PieceType.King)
        {
            _kings[(int)piece.Color] = square;
        }
    }

    internal void Configure(PlayerColor active, CastlingRights castling, int enPassant, int halfmove, int fullmove)
    {
        ActiveColor = active;
        Castling = castling;
        EnPassantSquare = enPassant;
        HalfmoveClock = halfmove;
        FullmoveNumber = fullmove;
    }

    public ChessBoard Clone()
    {
        var copy = new ChessBoard();
        Array.Copy(_squares, copy._squares, 64);
        copy._kings[0] = _kings[0];
        copy._kings[1] = _kings[1];
        copy.ActiveColor = ActiveColor;
        copy.Castling = Castling;
        copy.EnPassantSquare = EnPassantSquare;
        copy.HalfmoveClock = HalfmoveClock;
        copy.FullmoveNumber = FullmoveNumber;
        return copy;
    }

    /// <summary>Returns the board after playing a (legal) move; this instance is unchanged.</summary>
    public ChessBoard Apply(ChessMove move)
    {
        var next = Clone();
        next.MakeMove(move);
        return next;
    }

    private void MakeMove(ChessMove move)
    {
        var piece = _squares[move.From];
        var color = piece.Color;
        var resetsClock = piece.Type == PieceType.Pawn || !_squares[move.To].IsEmpty;

        if (move.IsEnPassant)
        {
            _squares[color == PlayerColor.White ? move.To - 8 : move.To + 8] = default;
            resetsClock = true;
        }

        Place(move.To, move.Promotion != PieceType.None ? new ChessPiece(move.Promotion, color) : piece);
        Place(move.From, default);

        if ((move.Flags & MoveFlags.KingsideCastle) != 0)
        {
            Place(move.From + 1, _squares[move.From + 3]);
            Place(move.From + 3, default);
        }
        else if ((move.Flags & MoveFlags.QueensideCastle) != 0)
        {
            Place(move.From - 1, _squares[move.From - 4]);
            Place(move.From - 4, default);
        }

        Castling &= ~(RightsLostOn(move.From) | RightsLostOn(move.To));
        EnPassantSquare = (move.Flags & MoveFlags.DoublePush) != 0 ? (move.From + move.To) / 2 : -1;
        HalfmoveClock = resetsClock ? 0 : HalfmoveClock + 1;
        if (color == PlayerColor.Black)
        {
            FullmoveNumber++;
        }

        ActiveColor = color.Opposite();
    }

    private static CastlingRights RightsLostOn(int square) => square switch
    {
        0 => CastlingRights.WhiteQueenside,
        7 => CastlingRights.WhiteKingside,
        4 => CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside,
        56 => CastlingRights.BlackQueenside,
        63 => CastlingRights.BlackKingside,
        60 => CastlingRights.BlackKingside | CastlingRights.BlackQueenside,
        _ => CastlingRights.None,
    };
}
