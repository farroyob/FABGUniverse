using FabGames.Core.Domain.Enums;

namespace FabGames.Games.Chess.Models;

public enum PieceType : byte
{
    None = 0,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

/// <summary>A piece on the board. <c>default</c> is the empty square.</summary>
public readonly record struct ChessPiece(PieceType Type, PlayerColor Color)
{
    public bool IsEmpty => Type == PieceType.None;

    public char ToFenChar()
    {
        var c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => ' ',
        };
        return Color == PlayerColor.White ? char.ToUpperInvariant(c) : c;
    }

    public static bool TryFromFenChar(char c, out ChessPiece piece)
    {
        var type = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => PieceType.None,
        };
        piece = type == PieceType.None
            ? default
            : new ChessPiece(type, char.IsUpper(c) ? PlayerColor.White : PlayerColor.Black);
        return type != PieceType.None;
    }
}

/// <summary>A square, indexed 0..63 with a1 = 0, b1 = 1, ..., h1 = 7, a2 = 8, ..., h8 = 63.</summary>
public readonly record struct ChessPosition(int Index)
{
    public int File => Index & 7;

    public int Rank => Index >> 3;

    public static ChessPosition From(int file, int rank) => new((rank << 3) | file);

    public static bool TryParse(string? text, out ChessPosition position)
    {
        position = default;
        if (text is not { Length: 2 })
        {
            return false;
        }

        var file = char.ToLowerInvariant(text[0]) - 'a';
        var rank = text[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7)
        {
            return false;
        }

        position = From(file, rank);
        return true;
    }

    public override string ToString() => $"{(char)('a' + File)}{Rank + 1}";
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    All = 15,
}

[Flags]
public enum MoveFlags : byte
{
    None = 0,
    Capture = 1,
    EnPassant = 2,
    KingsideCastle = 4,
    QueensideCastle = 8,
    DoublePush = 16,
}

/// <summary>Internal move representation used by the generator.</summary>
public readonly record struct ChessMove(int From, int To, PieceType Promotion = PieceType.None, MoveFlags Flags = MoveFlags.None)
{
    public bool IsCapture => (Flags & MoveFlags.Capture) != 0;

    public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;

    public bool IsCastle => (Flags & (MoveFlags.KingsideCastle | MoveFlags.QueensideCastle)) != 0;

    public override string ToString() =>
        $"{new ChessPosition(From)}{new ChessPosition(To)}{PromotionLetter(Promotion)}";

    public static string PromotionLetter(PieceType type) => type switch
    {
        PieceType.Queen => "q",
        PieceType.Rook => "r",
        PieceType.Bishop => "b",
        PieceType.Knight => "n",
        _ => string.Empty,
    };
}

/// <summary>The JSON action a client sends: squares like "e2" and an optional promotion letter (q, r, b, n).</summary>
public sealed record ChessMoveAction(string From, string To, string? Promotion = null);
