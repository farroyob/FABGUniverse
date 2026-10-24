namespace FabGames.Web.ViewModels;

/// <param name="Id">Game type id sent to the hub, or <c>soon</c> for the placeholder tile.</param>
/// <param name="Cover">Name of the key art (under /img/covers) used as the backdrop.</param>
public sealed record GameTileViewModel(
    string Id, string TitleKey, string DescriptionKey, int Players, string Cover, bool Available);

public sealed record HomeViewModel(IReadOnlyList<GameTileViewModel> Games);

/// <param name="RoomCode">Normalized code, or <c>new</c> when the page should create a room.</param>
/// <param name="IsNew">True for <c>/play/new</c>.</param>
/// <param name="Invalid">True when the URL holds a malformed code.</param>
public sealed record RoomViewModel(string RoomCode, bool IsNew, bool Invalid);

public sealed record ErrorViewModel(string RequestId);
