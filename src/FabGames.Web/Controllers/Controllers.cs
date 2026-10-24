using System.Diagnostics;
using FabGames.Core.Services;
using FabGames.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FabGames.Web.Controllers;

/// <summary>The console shell: boot, profile select and the game dashboard.</summary>
public sealed class HomeController : Controller
{
    private static readonly GameTileViewModel[] Tiles =
    [
        new("chess", "home.chess.title", "home.chess.desc", 2, "chess", true),
        new("checkers", "home.checkers.title", "home.checkers.desc", 2, "checkers", true),
        new("soon", "home.soon.title", "home.soon.desc", 0, "chess", false),
    ];

    public IActionResult Index() => View(new HomeViewModel(Tiles));

    public IActionResult Error() =>
        View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}

/// <summary>Hosts lobby, game and result for one room. All state arrives over SignalR.</summary>
public sealed class PlayController : Controller
{
    public IActionResult Room(string roomCode)
    {
        var isNew = string.Equals(roomCode, "new", StringComparison.OrdinalIgnoreCase);
        var code = isNew ? "new" : RoomCodeGenerator.Normalize(roomCode);
        return View(new RoomViewModel(code, isNew, code.Length == 0));
    }
}

/// <summary>Remembers the language choice and swaps the culture segment of the current URL.</summary>
public sealed class LocaleController : Controller
{
    public const string CookieName = "fab.culture";

    public IActionResult Set(string culture, string? returnUrl)
    {
        culture = culture is "en" ? "en" : "es";
        Response.Cookies.Append(CookieName, culture, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Secure = Request.IsHttps,
        });

        var target = "/" + culture;
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            && (returnUrl.StartsWith("/es", StringComparison.Ordinal) || returnUrl.StartsWith("/en", StringComparison.Ordinal))
            && (returnUrl.Length == 3 || returnUrl[3] is '/' or '?'))
        {
            target += returnUrl[3..];
        }

        return LocalRedirect(target);
    }
}
