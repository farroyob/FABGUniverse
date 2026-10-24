using System.Collections.Immutable;
using FabGames.Core.Domain.Enums;

namespace FabGames.Games.Checkers.Models;

public enum CheckersPieceType : byte
{
    None = 0,
    Man,
    King,
}

/// <summary>
/// A piece. <see cref="PlayerColor.Black"/> is the dark side (moves first, advances toward rank 1);
/// <see cref="PlayerColor.White"/> is the light side (advances toward rank 8).
/// </summary>
public readonly record struct CheckersPiece(CheckersPieceType Type, PlayerColor Color)
{
    public bool IsEmpty => Type == CheckersPieceType.None;

    public bool IsKing => Type == CheckersPieceType.King;
}

/// <summary>A square, indexed 0..63 with a1 = 0 and h8 = 63. Pieces only ever stand on dark squares.</summary>
public readonly record struct CheckersPosition(int Index)
{
    public int File => Index & 7;

    public int Rank => Index >> 3;

    public bool IsDark => ((File + Rank) & 1) == 0;

    public static CheckersPosition From(int file, int rank) => new((rank << 3) | file);

    public static bool TryParse(string? text, out CheckersPosition position)
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

/// <summary>The JSON action: the origin square and the full path of landing squares ("from", ["to1", "to2", ...]).</summary>
public sealed record CheckersMoveAction(string From, IReadOnlyList<string> Path);

/// <summary>A complete legal turn: a single step, or a full capture sequence.</summary>
public sealed record CheckersMove(
    int From, ImmutableArray<int> Path, ImmutableArray<int> Captured, bool Promotes)
{
    public int To => Path[^1];

    public bool IsCapture => !Captured.IsDefaultOrEmpty;

    public bool Matches(int from, IReadOnlyList<int> path)
    {
        if (From != from || Path.Length != path.Count)
        {
            return false;
        }

        for (var i = 0; i < path.Count; i++)
        {
            if (Path[i] != path[i])
            {
                return false;
            }
        }

        return true;
    }

    public CheckersMoveAction ToAction() =>
        new(new CheckersPosition(From).ToString(), [.. Path.Select(p => new CheckersPosition(p).ToString())]);

    public string ToNotation() =>
        new CheckersPosition(From) + string.Concat(Path.Select(p => (IsCapture ? "x" : "-") + new CheckersPosition(p)));
}
