using FabGames.Core.Domain.Results;

namespace FabGames.Core.Interfaces;

/// <summary>
/// A turn-based game implementation. Engines are stateless singletons: all game state lives in the
/// immutable state object they create. A game action is one complete turn (for example a whole
/// multi-jump), so the turn passes atomically.
/// </summary>
public interface IGameEngine
{
    /// <summary>Stable lowercase identifier, for example <c>chess</c>.</summary>
    string GameTypeId { get; }

    int MinPlayers { get; }

    int MaxPlayers { get; }

    /// <summary>Creates the starting state. Index <c>i</c> of <paramref name="playerIdsByColor"/> plays <c>PlayerColor</c> <c>i</c>.</summary>
    object CreateInitialState(IReadOnlyList<string> playerIdsByColor);

    /// <summary>Validates and applies a JSON action. Never throws for bad client input.</summary>
    GameActionResult ApplyAction(object state, string playerId, string actionJson);

    string GetCurrentTurnPlayerId(object state);

    /// <summary>Legal actions for <paramref name="playerId"/>; empty when it is not their turn.</summary>
    IReadOnlyList<object> GetLegalActions(object state, string playerId);

    /// <summary>The result if the game has ended, otherwise null.</summary>
    GameResult? GetResult(object state);

    GameResult Resign(object state, string playerId);

    /// <summary>Result when <paramref name="playerId"/> runs out of time.</summary>
    GameResult DeclareTimeout(object state, string playerId);
}

/// <summary>Typed counterpart of <see cref="IGameEngine"/>.</summary>
public interface IGameEngine<TState, TAction> : IGameEngine
    where TState : class
    where TAction : class
{
    TState NewGame(IReadOnlyList<string> playerIdsByColor);

    GameActionResult<TState> Apply(TState state, string playerId, TAction action);

    string GetCurrentTurnPlayerId(TState state);

    IReadOnlyList<TAction> GetLegalActions(TState state, string playerId);

    GameResult? GetResult(TState state);

    GameResult Resign(TState state, string playerId);

    GameResult DeclareTimeout(TState state, string playerId);
}
