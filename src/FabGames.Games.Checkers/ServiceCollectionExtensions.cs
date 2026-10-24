using FabGames.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FabGames.Games.Checkers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCheckersGame(this IServiceCollection services) =>
        services.AddGameEngine<CheckersEngine>();
}
