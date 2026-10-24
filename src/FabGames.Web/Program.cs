using System.Globalization;
using FabGames.Core;
using FabGames.Core.Services;
using FabGames.Games.Checkers;
using FabGames.Games.Chess;
using FabGames.Web.BackgroundServices;
using FabGames.Web.Hubs;
using FabGames.Web.Mapping;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews().AddViewLocalization();

// The culture comes from the first URL segment: /es/... or /en/...
CultureInfo[] cultures = [new("es"), new("en")];
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("es");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.RequestCultureProviders = [new RouteDataRequestCultureProvider { Options = options }];
});

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
    app.UseExceptionHandler("/es/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// No inline scripts or styles anywhere, so the policy can be strict.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self' ws: wss:; media-src 'self'; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseRouting();
app.UseRequestLocalization();
app.UseAuthorization();

app.MapStaticAssets();

// "/" goes to the visitor's language: saved choice, then Accept-Language, then Spanish.
app.MapGet("/", (HttpContext context) =>
{
    var saved = context.Request.Cookies[FabGames.Web.Controllers.LocaleController.CookieName];
    var culture = saved is "es" or "en"
        ? saved
        : context.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value?[..Math.Min(2, l.Value.Value.Length)].ToLowerInvariant())
            .FirstOrDefault(c => c is "es" or "en") ?? "es";
    return Results.Redirect($"/{culture}");
});

app.MapControllerRoute(
    name: "play",
    pattern: "{culture:regex(^(es|en)$)}/play/{roomCode}",
    defaults: new { controller = "Play", action = "Room" });

app.MapControllerRoute(
    name: "localized",
    pattern: "{culture:regex(^(es|en)$)}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "locale",
    pattern: "locale/{culture}",
    defaults: new { controller = "Locale", action = "Set" });

// Same-origin hosting: no CORS policy is needed. Add a named policy only if a separate client origin is configured.
app.MapHub<GameHub>("/hubs/game");

app.Run();

/// <summary>Entry point marker so integration tests can host the app.</summary>
public partial class Program;
