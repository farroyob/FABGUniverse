using FabGames.Core;
using FabGames.Core.Services;
using FabGames.Games.Checkers;
using FabGames.Games.Chess;
using FabGames.Web.BackgroundServices;
using FabGames.Web.Hubs;
using FabGames.Web.Mapping;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Core: rooms and engines. Adding a game is one engine registration plus one state mapper.
builder.Services.AddFabGamesCore();
builder.Services.Configure<RoomManagerOptions>(builder.Configuration.GetSection(RoomManagerOptions.SectionName));
builder.Services.AddChessGame();
builder.Services.AddCheckersGame();
builder.Services.AddSingleton<IGameStateMapper, ChessStateMapper>();
builder.Services.AddSingleton<IGameStateMapper, CheckersStateMapper>();

// Transport.
builder.Services.Configure<HubRateLimitOptions>(builder.Configuration.GetSection(HubRateLimitOptions.SectionName));
builder.Services.AddSingleton<ConnectionRateLimiter>();
builder.Services.AddSingleton<RateLimitHubFilter>();
builder.Services.AddSingleton<RoomViewMapper>();
builder.Services.AddSingleton<RoomBroadcaster>();
builder.Services.AddSignalR(options => options.AddFilter<RateLimitHubFilter>());

// Background work.
builder.Services.AddSingleton<TurnClockService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TurnClockService>());
builder.Services.AddSingleton<RoomCleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RoomCleanupService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Same-origin hosting: no CORS policy is needed. Add a named policy only if a separate client origin is configured.
app.MapHub<GameHub>("/hubs/game");

app.Run();

/// <summary>Entry point marker so integration tests can host the app.</summary>
public partial class Program;
