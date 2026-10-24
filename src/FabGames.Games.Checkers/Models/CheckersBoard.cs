using FabGames.Core.Domain.Enums;
using FabGames.Games.Checkers.Logic;

namespace FabGames.Games.Checkers.Models;

/// <summary>Piece placement. Treated as immutable once published: <see cref="Apply"/> returns a new board.</summary>
public sealed class CheckersBoard
{
    private readonly CheckersPiece[] _squares = new CheckersPiece[64];

    public CheckersPiece this[int square] => _squares[square];

    public static CheckersBoard CreateStart()
    {
        var board = new CheckersBoard();
        for (var sq = 0; sq < 64; sq++)
        {
            var position = new CheckersPosition(sq);
            if (!position.IsDark)
            {
                continue;
            }

            if (position.Rank <= 2)
            {
                board._squares[sq] = new CheckersPiece(CheckersPieceType.Man, PlayerColor.White);
            }
            else if (position.Rank >= 5)
            {
                board._squares[sq] = new CheckersPiece(CheckersPieceType.Man, PlayerColor.Black);
            }
        }

        return board;
    }

    /// <summary>
    /// Parses an 8-line diagram, rank 8 first. <c>b</c>/<c>B</c> = dark man/king, <c>w</c>/<c>W</c> = light
    /// man/king, <c>.</c> = empty. Whitespace between squares is ignored.
    /// </summary>
    public static CheckersBoard Parse(string diagram)
    {
        var board = new CheckersBoard();
        var rows = diagram.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length != 8)
        {
            throw new FormatException("A checkers diagram needs 8 rows.");
        }

        for (var i = 0; i < 8; i++)
        {
            var cells = rows[i].Replace(" ", string.Empty);
            if (cells.Length != 8)
            {
                throw new FormatException($"Row {i + 1} needs 8 squares.");
            }

            for (var file = 0; file < 8; file++)
            {
                var piece = cells[file] switch
                {
                    'b' => new CheckersPiece(CheckersPieceType.Man, PlayerColor.Black),
                    'B' => new CheckersPiece(CheckersPieceType.King, PlayerColor.Black),
                    'w' => new CheckersPiece(CheckersPieceType.Man, PlayerColor.White),
                    'W' => new CheckersPiece(CheckersPieceType.King, PlayerColor.White),
                    '.' => default,
                    _ => throw new FormatException($"Unknown square '{cells[file]}'."),
                };
                var square = CheckersPosition.From(file, 7 - i);
                if (!piece.IsEmpty && !square.IsDark)
                {
                    throw new FormatException($"{square} is a light square; pieces stand on dark squares only.");
                }

                board._squares[square.Index] = piece;
            }
        }

        return board;
    }

    public int Count(PlayerColor color)
    {
        var count = 0;
        foreach (var piece in _squares)
        {
            if (!piece.IsEmpty && piece.Color == color)
            {
                count++;
            }
        }

        return count;
    }

    public CheckersBoard Apply(CheckersMove move)
    {
        var next = new CheckersBoard();
        Array.Copy(_squares, next._squares, 64);
        var piece = _squares[move.From];
        next._squares[move.From] = default;
        foreach (var captured in move.Captured)
        {
            next._squares[captured] = default;
        }

        next._squares[move.To] = move.Promotes ? new CheckersPiece(CheckersPieceType.King, piece.Color) : piece;
        return next;
    }
}
