using System.Text;
using FabGames.Core.Domain.Enums;
using FabGames.Games.Chess.Models;

namespace FabGames.Games.Chess.Logic;

/// <summary>Loads and exports positions in Forsyth–Edwards Notation.</summary>
public static class FenParser
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static ChessBoard Parse(string fen)
    {
        return TryParse(fen, out var board, out var error)
            ? board
            : throw new FormatException($"Invalid FEN: {error}");
    }

    public static bool TryParse(string? fen, out ChessBoard board, out string error)
    {
        board = null!;
        var fields = fen?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (fields.Length is < 4 or > 6)
        {
            error = "expected 4 to 6 fields";
            return false;
        }

        var result = new ChessBoard();
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8)
        {
            error = "expected 8 ranks";
            return false;
        }

        var kings = new int[2];
        for (var i = 0; i < 8; i++)
        {
            var rank = 7 - i;
            var file = 0;
            foreach (var c in ranks[i])
            {
                if (char.IsAsciiDigit(c) && c is >= '1' and <= '8')
                {
                    file += c - '0';
                    continue;
                }

                if (file > 7 || !ChessPiece.TryFromFenChar(c, out var piece))
                {
                    error = $"bad placement in rank {rank + 1}";
                    return false;
                }

                if (piece.Type == PieceType.Pawn && rank is 0 or 7)
                {
                    error = "pawn on the first or last rank";
                    return false;
                }

                if (piece.Type == PieceType.King)
                {
                    kings[(int)piece.Color]++;
                }

                result.Place((rank << 3) | file, piece);
                file++;
            }

            if (file != 8)
            {
                error = $"rank {rank + 1} does not have 8 squares";
                return false;
            }
        }

        if (kings[0] != 1 || kings[1] != 1)
        {
            error = "each side needs exactly one king";
            return false;
        }

        var active = fields[1] switch
        {
            "w" => PlayerColor.White,
            "b" => PlayerColor.Black,
            _ => (PlayerColor?)null,
        };
        if (active is null)
        {
            error = "side to move must be w or b";
            return false;
        }

        var castling = CastlingRights.None;
        if (fields[2] != "-")
        {
            foreach (var c in fields[2])
            {
                var right = c switch
                {
                    'K' => CastlingRights.WhiteKingside,
                    'Q' => CastlingRights.WhiteQueenside,
                    'k' => CastlingRights.BlackKingside,
                    'q' => CastlingRights.BlackQueenside,
                    _ => CastlingRights.None,
                };
                if (right == CastlingRights.None)
                {
                    error = "bad castling field";
                    return false;
                }

                castling |= right;
            }
        }

        var enPassant = -1;
        if (fields[3] != "-")
        {
            if (!ChessPosition.TryParse(fields[3], out var ep) || ep.Rank is not (2 or 5))
            {
                error = "bad en passant square";
                return false;
            }

            enPassant = ep.Index;
        }

        var halfmove = 0;
        var fullmove = 1;
        if ((fields.Length > 4 && (!int.TryParse(fields[4], out halfmove) || halfmove < 0))
            || (fields.Length > 5 && (!int.TryParse(fields[5], out fullmove) || fullmove < 1)))
        {
            error = "bad move counters";
            return false;
        }

        result.Configure(active.Value, castling, enPassant, halfmove, fullmove);
        board = result;
        error = string.Empty;
        return true;
    }

    public static string ToFen(ChessBoard board) =>
        $"{Placement(board)} {(board.ActiveColor == PlayerColor.White ? 'w' : 'b')} {CastlingField(board.Castling)} " +
        $"{(board.EnPassantSquare < 0 ? "-" : new ChessPosition(board.EnPassantSquare).ToString())} " +
        $"{board.HalfmoveClock} {board.FullmoveNumber}";

    /// <summary>First four FEN fields: enough to identify a position for repetition purposes.</summary>
    internal static string Placement(ChessBoard board)
    {
        var sb = new StringBuilder(72);
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = board[(rank << 3) | file];
                if (piece.IsEmpty)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }

                sb.Append(piece.ToFenChar());
            }

            if (empty > 0)
            {
                sb.Append(empty);
            }

            if (rank > 0)
            {
                sb.Append('/');
            }
        }

        return sb.ToString();
    }

    internal static string CastlingField(CastlingRights rights)
    {
        if (rights == CastlingRights.None)
        {
            return "-";
        }

        var sb = new StringBuilder(4);
        if ((rights & CastlingRights.WhiteKingside) != 0) { sb.Append('K'); }
        if ((rights & CastlingRights.WhiteQueenside) != 0) { sb.Append('Q'); }
        if ((rights & CastlingRights.BlackKingside) != 0) { sb.Append('k'); }
        if ((rights & CastlingRights.BlackQueenside) != 0) { sb.Append('q'); }
        return sb.ToString();
    }
}
