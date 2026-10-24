using FabGames.Core.Interfaces;

namespace FabGames.Core.Services;

public sealed class GameEngineRegistry : IGameEngineRegistry
{
    private readonly Dictionary<string, IGameEngine> _engines;

    public GameEngineRegistry(IEnumerable<IGameEngine> engines)
    {
        _engines = engines.ToDictionary(e => e.GameTypeId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IGameEngine> Engines => _engines.Values;

    public bool TryGet(string gameTypeId, out IGameEngine engine)
    {
        if (gameTypeId is not null && _engines.TryGetValue(gameTypeId, out var found))
        {
            engine = found;
            return true;
        }

        engine = null!;
        return false;
    }
}
