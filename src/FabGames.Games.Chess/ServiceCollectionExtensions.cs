using FabGames.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FabGames.Games.Chess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChessGame(this IServiceCollection services) =>
        services.AddGameEngine<ChessEngine>();
}
