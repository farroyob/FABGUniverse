using FabGames.Core.Interfaces;
using FabGames.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FabGames.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the room manager and the engine registry. Add engines with <see cref="AddGameEngine{TEngine}"/>.</summary>
    public static IServiceCollection AddFabGamesCore(
        this IServiceCollection services, Action<RoomManagerOptions>? configure = null)
    {
        services.AddOptions<RoomManagerOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IGameEngineRegistry, GameEngineRegistry>();
        services.TryAddSingleton<IRoomManager, RoomManager>();
        return services;
    }

    /// <summary>The single registration a new game needs.</summary>
    public static IServiceCollection AddGameEngine<TEngine>(this IServiceCollection services)
        where TEngine : class, IGameEngine =>
        services.AddSingleton<IGameEngine, TEngine>();
}
