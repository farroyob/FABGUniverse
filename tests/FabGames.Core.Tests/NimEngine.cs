using System.Collections.Immutable;
using FabGames.Core.Domain.Results;
using FabGames.Core.Domain.Enums;
using FabGames.Core.Services;

namespace FabGames.Core.Tests;

public sealed record NimAction(int Take);

public sealed record NimState(int Stones, ImmutableArray<string> Players, int TurnIndex, GameResult? Result);

/// <summary>A deliberately tiny third game: take 1-3 stones, whoever takes the last one wins.</summary>
public sealed class NimEngine : GameEngineBase<NimState, NimAction>
{
    public const int StartStones = 21;

    public override string GameTypeId => "nim";

    public override int MinPlayers => 2;

    public override int MaxPlayers => 2;

    public override NimState NewGame(IReadOnlyList<string> playerIdsByColor) =>
        new(StartStones, [.. playerIdsByColor], 0, null);

    public override GameActionResult<NimState> Apply(NimState state, string playerId, NimAction action)
    {
        if (state.Result is not null)
        {
            return GameActionResult<NimState>.Fail(ErrorCodes.GameOver);
        }

        if (state.Players[state.TurnIndex] != playerId)
        {
            return GameActionResult<NimState>.Fail(ErrorCodes.NotYourTurn);
        }

        if (action.Take is < 1 or > 3 || action.Take > state.Stones)
        {
            return GameActionResult<NimState>.Fail(ErrorCodes.IllegalMove);
        }

        var stones = state.Stones - action.Take;
        GameResult? result = stones == 0 ? GameResult.Win(playerId, GameEndReason.NoLegalMoves) : null;
        var next = state with { Stones = stones, TurnIndex = 1 - state.TurnIndex, Result = result };
        return GameActionResult<NimState>.Ok(next, result);
    }

    public override string GetCurrentTurnPlayerId(NimState state) => state.Players[state.TurnIndex];

    public override IReadOnlyList<NimAction> GetLegalActions(NimState state, string playerId) =>
        state.Result is null && state.Players[state.TurnIndex] == playerId
            ? [.. Enumerable.Range(1, Math.Min(3, state.Stones)).Select(n => new NimAction(n))]
            : [];

    public override GameResult? GetResult(NimState state) => state.Result;

    public override GameResult Resign(NimState state, string playerId) =>
        GameResult.Win(state.Players.Single(p => p != playerId), GameEndReason.Resignation);

    public override GameResult DeclareTimeout(NimState state, string playerId) =>
        GameResult.Win(state.Players.Single(p => p != playerId), GameEndReason.Timeout);
}
