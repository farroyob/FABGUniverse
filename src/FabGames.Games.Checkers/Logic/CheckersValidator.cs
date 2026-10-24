using FabGames.Core.Domain.Results;
using FabGames.Games.Checkers.Models;

namespace FabGames.Games.Checkers.Logic;

/// <summary>Maps a client action onto one of the legal turns, or explains why it cannot.</summary>
public static class CheckersValidator
{
    private const int MaxPathLength = 16;

    public static bool TryResolve(
        IReadOnlyList<CheckersMove> legalMoves, CheckersMoveAction action, out CheckersMove move, out string errorCode)
    {
        move = null!;
        if (!CheckersPosition.TryParse(action.From, out var from)
            || action.Path is not { Count: > 0 and <= MaxPathLength } rawPath)
        {
            errorCode = ErrorCodes.InvalidAction;
            return false;
        }

        var path = new List<int>(rawPath.Count);
        foreach (var square in rawPath)
        {
            if (!CheckersPosition.TryParse(square, out var position))
            {
                errorCode = ErrorCodes.InvalidAction;
                return false;
            }

            path.Add(position.Index);
        }

        foreach (var candidate in legalMoves)
        {
            if (candidate.Matches(from.Index, path))
            {
                move = candidate;
                errorCode = string.Empty;
                return true;
            }
        }

        errorCode = IsPrefixOfCapture(legalMoves, from.Index, path)
            ? ErrorCodes.IncompleteCapture
            : legalMoves is [{ IsCapture: true }, ..] ? ErrorCodes.MandatoryCapture : ErrorCodes.IllegalMove;
        return false;
    }

    private static bool IsPrefixOfCapture(IReadOnlyList<CheckersMove> legalMoves, int from, List<int> path)
    {
        foreach (var candidate in legalMoves)
        {
            if (!candidate.IsCapture || candidate.From != from || candidate.Path.Length <= path.Count)
            {
                continue;
            }

            var prefix = true;
            for (var i = 0; i < path.Count && prefix; i++)
            {
                prefix = candidate.Path[i] == path[i];
            }

            if (prefix)
            {
                return true;
            }
        }

        return false;
    }
}
