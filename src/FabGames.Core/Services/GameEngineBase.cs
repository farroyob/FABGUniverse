using System.Text.Json;
using FabGames.Core.Domain.Results;
using FabGames.Core.Interfaces;

namespace FabGames.Core.Services;

/// <summary>
/// Implements the untyped <see cref="IGameEngine"/> members (JSON action parsing, state casting) so a
/// game only writes typed logic.
/// </summary>
public abstract class GameEngineBase<TState, TAction> : IGameEngine<TState, TAction>
    where TState : class
    where TAction : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public abstract string GameTypeId { get; }

    public abstract int MinPlayers { get; }

    public abstract int MaxPlayers { get; }

    public abstract TState NewGame(IReadOnlyList<string> playerIdsByColor);

    public abstract GameActionResult<TState> Apply(TState state, string playerId, TAction action);

    public abstract string GetCurrentTurnPlayerId(TState state);

    public abstract IReadOnlyList<TAction> GetLegalActions(TState state, string playerId);

    public abstract GameResult? GetResult(TState state);

    public abstract GameResult Resign(TState state, string playerId);

    public abstract GameResult DeclareTimeout(TState state, string playerId);

    object IGameEngine.CreateInitialState(IReadOnlyList<string> playerIdsByColor) => NewGame(playerIdsByColor);

    GameActionResult IGameEngine.ApplyAction(object state, string playerId, string actionJson)
    {
        TAction? action;
        try
        {
            action = JsonSerializer.Deserialize<TAction>(actionJson, JsonOptions);
        }
        catch (JsonException)
        {
            return GameActionResult.Fail(ErrorCodes.InvalidAction);
        }

        if (action is null)
        {
            return GameActionResult.Fail(ErrorCodes.InvalidAction);
        }

        var typed = Apply(Cast(state), playerId, action);
        return typed.Success
            ? GameActionResult.Ok(typed.NewState!, typed.Result)
            : GameActionResult.Fail(typed.ErrorCode ?? ErrorCodes.IllegalMove);
    }

    string IGameEngine.GetCurrentTurnPlayerId(object state) => GetCurrentTurnPlayerId(Cast(state));

    IReadOnlyList<object> IGameEngine.GetLegalActions(object state, string playerId) =>
        [.. GetLegalActions(Cast(state), playerId)];

    GameResult? IGameEngine.GetResult(object state) => GetResult(Cast(state));

    GameResult IGameEngine.Resign(object state, string playerId) => Resign(Cast(state), playerId);

    GameResult IGameEngine.DeclareTimeout(object state, string playerId) =>
        DeclareTimeout(Cast(state), playerId);

    private static TState Cast(object state) =>
        state as TState
        ?? throw new ArgumentException($"State must be {typeof(TState).Name}.", nameof(state));
}
